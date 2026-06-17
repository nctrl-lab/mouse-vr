#!/usr/bin/env python3
"""Water-valve calibration GUI for the beacon-task Teensy firmware.

Mirrors the structure of nctrl-lab/ibl-task ibl/calibrate.py (PyQt6, per-target
rows, manual dispense-then-weigh workflow) but talks directly to *this* firmware
(teensy.ino) over USB serial. No ibl.io dependency.

Firmware commands used (see the sketch's help):
    d<usec>\\n  set the water-valve open duration in microseconds (1000..10000000)
    w\\n        give one water reward (opens the valve for the set duration)
    i\\n        open the valve for 1 s (manual prime / flush)
    0\\n        reset all outputs off

Workflow: pick a target volume, set a pulse duration, dispense N pulses (~1 mL
total) into a tared container, weigh it (1 mg water ~= 1 ul), divide the weight by
N for ul/pulse, adjust the duration, and transcribe it into the firmware's
waterAmount[] table.

Requires:  pip install pyserial PyQt6
Run:       python calibrate.py
"""

import json
import sys
from dataclasses import asdict, dataclass
from pathlib import Path

try:
    import serial
    from serial.tools import list_ports
except ImportError:  # pragma: no cover - dependency hint
    serial = None
    list_ports = None

from PyQt6.QtCore import QTimer
from PyQt6.QtWidgets import (
    QApplication, QComboBox, QDoubleSpinBox, QFrame, QHBoxLayout, QLabel,
    QMainWindow, QMessageBox, QPlainTextEdit, QPushButton, QSpinBox,
    QVBoxLayout, QWidget,
)

BAUD = 115200
CONFIG_PATH = Path.home() / ".config" / "mouse-vr" / "water_calibration.json"

# 'd' command bounds (microseconds), from the firmware validation in checkSerial().
DUR_MIN_US = 1_000
DUR_MAX_US = 10_000_000

# Each calibration run aims to dispense ~this total so the scale reading is precise
# (1 mg water ~= 1 ul). n_pulses defaults to total / target_ul for each row.
CALIB_TOTAL_UL = 1000.0  # 1 mL


def pulses_for_total(target_ul, total_ul=CALIB_TOTAL_UL):
    """Pulse count whose combined volume is ~total_ul at target_ul per pulse."""
    if target_ul <= 0:
        return 1
    return max(1, round(total_ul / target_ul))


# --------------------------------------------------------------------------- #
# Hardware
# --------------------------------------------------------------------------- #
class Teensy:
    """Thin serial wrapper around the firmware's water commands."""

    def __init__(self, port, baud=BAUD):
        if serial is None:
            raise RuntimeError("pyserial is not installed (pip install pyserial)")
        self.ser = serial.Serial(port, baud, timeout=0.05)

    def close(self):
        try:
            self.reset()
        except Exception:
            pass
        self.ser.close()

    def _write(self, text):
        self.ser.write(text.encode("ascii"))
        self.ser.flush()

    def set_duration_us(self, us):
        us = int(max(DUR_MIN_US, min(DUR_MAX_US, us)))
        self._write(f"d{us}\n")

    def reward(self):
        self._write("w\n")

    def open_1s(self):
        self._write("i\n")

    def reset(self):
        self._write("0\n")

    def read_lines(self):
        lines = []
        try:
            while self.ser.in_waiting:
                raw = self.ser.readline().decode("ascii", "replace").strip()
                if raw:
                    lines.append(raw)
        except Exception:
            pass
        return lines


class FakeTeensy:
    """Offline stand-in so the GUI runs without hardware."""

    def __init__(self, *args, **kwargs):
        self._log = ["Ready (fake)"]
        self._dur = 65000

    def close(self):
        pass

    def set_duration_us(self, us):
        self._dur = int(max(DUR_MIN_US, min(DUR_MAX_US, us)))
        self._log.append(f"Water duration: {self._dur}")

    def reward(self):
        pass

    def open_1s(self):
        pass

    def reset(self):
        pass

    def read_lines(self):
        out, self._log = self._log, []
        return out


# --------------------------------------------------------------------------- #
# Calibration row
# --------------------------------------------------------------------------- #
@dataclass
class Target:
    target_ul: float = 5.0
    duration_ms: float = 60.0
    n_pulses: int = 200


class TargetRow(QFrame):
    """One calibration target: dispense N pulses, then weigh externally."""

    def __init__(self, window, target: Target):
        super().__init__()
        self.window = window
        self.setFrameShape(QFrame.Shape.StyledPanel)

        self.target_ul = QDoubleSpinBox(decimals=1, suffix=" ul")
        self.target_ul.setRange(0.1, 50.0)
        self.target_ul.setValue(target.target_ul)

        self.duration_ms = QDoubleSpinBox(decimals=1, suffix=" ms")
        self.duration_ms.setRange(DUR_MIN_US / 1000.0, DUR_MAX_US / 1000.0)
        self.duration_ms.setValue(target.duration_ms)

        self.n_pulses = QSpinBox()
        self.n_pulses.setRange(1, 5000)
        self.n_pulses.setValue(target.n_pulses)
        # Auto-fill pulse count for ~1 mL whenever the target volume changes.
        # Connected here (after the setValue above) so a loaded n_pulses is preserved
        # on startup and only recomputed on a manual target edit.
        self.target_ul.valueChanged.connect(self._sync_pulses)

        self.dispense_btn = QPushButton("Dispense")
        self.dispense_btn.clicked.connect(self._toggle)

        self.result = QLabel("-")
        self.result.setMinimumWidth(260)

        remove_btn = QPushButton("x")
        remove_btn.setFixedWidth(28)
        remove_btn.clicked.connect(lambda: self.window.remove_row(self))

        self._count = 0
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._pulse)

        layout = QHBoxLayout(self)
        for label, widget in (
            ("target", self.target_ul),
            ("duration", self.duration_ms),
            ("pulses", self.n_pulses),
        ):
            layout.addWidget(QLabel(label))
            layout.addWidget(widget)
        layout.addWidget(self.dispense_btn)
        layout.addWidget(self.result, 1)
        layout.addWidget(remove_btn)

    # -- dispense control --------------------------------------------------- #
    def _toggle(self):
        if self._timer.isActive():
            self._stop("cancelled")
            return
        hw = self.window.hw
        if hw is None:
            self.window.warn("Not connected.")
            return
        hw.set_duration_us(self.duration_ms.value() * 1000.0)
        self._count = 0
        # keep pulses separated: never fire faster than the valve can close
        interval_ms = max(self.window.gap_ms(), self.duration_ms.value() + 50.0)
        self.dispense_btn.setText("Stop")
        self._set_inputs_enabled(False)
        self._timer.start(int(interval_ms))
        self._pulse()  # fire the first one immediately

    def _pulse(self):
        self.window.hw.reward()
        self._count += 1
        self.result.setText(f"dispensing... {self._count}/{self.n_pulses.value()}")
        if self._count >= self.n_pulses.value():
            self._stop("done")

    def _stop(self, why):
        self._timer.stop()
        self.dispense_btn.setText("Dispense")
        self._set_inputs_enabled(True)
        self.window.log(f"dispense {why}: {self._count} pulses @ "
                        f"{self.duration_ms.value():.1f} ms")
        self.result.setText(f"{why}: {self._count} pulses")

    def _set_inputs_enabled(self, on):
        for w in (self.target_ul, self.duration_ms, self.n_pulses):
            w.setEnabled(on)

    # -- pulse count -------------------------------------------------------- #
    def _sync_pulses(self):
        # keep the total dispensed volume ~CALIB_TOTAL_UL (1 mL) as the target changes
        self.n_pulses.setValue(pulses_for_total(self.target_ul.value()))

    def to_target(self):
        return Target(
            target_ul=self.target_ul.value(),
            duration_ms=self.duration_ms.value(),
            n_pulses=self.n_pulses.value(),
        )


# --------------------------------------------------------------------------- #
# Main window
# --------------------------------------------------------------------------- #
class CalibWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("Teensy water-valve calibration")
        self.hw = None
        self.rows = []

        central = QWidget()
        self.setCentralWidget(central)
        root = QVBoxLayout(central)

        # connection bar
        bar = QHBoxLayout()
        self.port = QComboBox()
        self.port.setEditable(True)
        self.port.setMinimumWidth(180)
        refresh = QPushButton("Refresh")
        refresh.clicked.connect(self._refresh_ports)
        self.connect_btn = QPushButton("Connect")
        self.connect_btn.clicked.connect(self._toggle_connect)
        self.fake_btn = QPushButton("Fake")
        self.fake_btn.clicked.connect(self._connect_fake)
        self.status = QLabel("disconnected")
        bar.addWidget(QLabel("port"))
        bar.addWidget(self.port)
        bar.addWidget(refresh)
        bar.addWidget(self.connect_btn)
        bar.addWidget(self.fake_btn)
        bar.addWidget(self.status, 1)
        root.addLayout(bar)

        # global controls + manual buttons
        ctrl = QHBoxLayout()
        self.gap = QSpinBox()
        self.gap.setRange(50, 5000)
        self.gap.setValue(300)
        self.gap.setSuffix(" ms gap")
        ctrl.addWidget(QLabel("gap"))
        ctrl.addWidget(self.gap)
        for text, slot in (
            ("Single reward (w)", self._manual_reward),
            ("Open 1 s (i)", self._manual_open),
            ("Stop dispensing", self._stop_all),
            ("Reset (0)", self._manual_reset),
        ):
            btn = QPushButton(text)
            btn.clicked.connect(slot)
            ctrl.addWidget(btn)
        ctrl.addStretch(1)
        root.addLayout(ctrl)

        # rows
        self.rows_box = QVBoxLayout()
        root.addLayout(self.rows_box)
        row_ctrl = QHBoxLayout()
        add_btn = QPushButton("Add target")
        add_btn.clicked.connect(lambda: self.add_row(Target()))
        save_btn = QPushButton("Save")
        save_btn.clicked.connect(self._save)
        row_ctrl.addWidget(add_btn)
        row_ctrl.addWidget(save_btn)
        row_ctrl.addStretch(1)
        root.addLayout(row_ctrl)

        # serial log
        self.console = QPlainTextEdit()
        self.console.setReadOnly(True)
        self.console.setMaximumBlockCount(500)
        root.addWidget(self.console, 1)

        self.poll = QTimer(self)
        self.poll.timeout.connect(self._poll_serial)
        self.poll.start(100)

        self._refresh_ports()
        self._load()

    # -- ports / connection ------------------------------------------------- #
    def _refresh_ports(self):
        self.port.clear()
        if list_ports is not None:
            for p in list_ports.comports():
                self.port.addItem(p.device)
        if self.port.count() == 0:
            self.port.addItem("/dev/ttyACM0")

    def _toggle_connect(self):
        if self.hw is not None:
            self._disconnect()
            return
        try:
            self.hw = Teensy(self.port.currentText().strip())
        except Exception as exc:
            self.warn(f"Connect failed: {exc}")
            self.hw = None
            return
        self._on_connected(self.port.currentText().strip())

    def _connect_fake(self):
        if self.hw is not None:
            self._disconnect()
        self.hw = FakeTeensy()
        self._on_connected("fake")

    def _on_connected(self, label):
        self.status.setText(f"connected: {label}")
        self.connect_btn.setText("Disconnect")
        self.log(f"connected to {label}")

    def _disconnect(self):
        for row in self.rows:
            if row._timer.isActive():
                row._stop("disconnect")
        if self.hw is not None:
            try:
                self.hw.close()
            except Exception:
                pass
        self.hw = None
        self.status.setText("disconnected")
        self.connect_btn.setText("Connect")

    # -- manual buttons ----------------------------------------------------- #
    def _manual_reward(self):
        if self.hw:
            self.hw.reward()

    def _manual_open(self):
        if self.hw:
            self.hw.open_1s()

    def _manual_reset(self):
        if self.hw:
            self.hw.reset()

    def _stop_all(self):
        # Global stop: halt every row's pulse timer and force the valve closed.
        stopped = sum(1 for row in self.rows if row._timer.isActive())
        for row in self.rows:
            if row._timer.isActive():
                row._stop("stopped")
        if self.hw:
            self.hw.reset()
        self.log(f"stopped all dispensing ({stopped} active)")

    # -- rows --------------------------------------------------------------- #
    def add_row(self, target):
        row = TargetRow(self, target)
        self.rows.append(row)
        self.rows_box.addWidget(row)
        return row

    def remove_row(self, row):
        if row._timer.isActive():
            row._stop("removed")
        self.rows.remove(row)
        row.setParent(None)

    # -- serial log --------------------------------------------------------- #
    def _poll_serial(self):
        if self.hw is None:
            return
        for line in self.hw.read_lines():
            self.console.appendPlainText(line)

    def log(self, msg):
        self.console.appendPlainText(f"# {msg}")

    def warn(self, msg):
        QMessageBox.warning(self, "Calibration", msg)

    # -- persistence -------------------------------------------------------- #
    def gap_ms(self):
        return float(self.gap.value())

    def _save(self):
        data = {
            "gap_ms": self.gap.value(),
            "targets": [asdict(r.to_target()) for r in self.rows],
        }
        CONFIG_PATH.parent.mkdir(parents=True, exist_ok=True)
        CONFIG_PATH.write_text(json.dumps(data, indent=2) + "\n")
        self.log(f"saved {CONFIG_PATH}")

    def _load(self):
        if CONFIG_PATH.exists():
            try:
                data = json.loads(CONFIG_PATH.read_text())
            except Exception as exc:
                self.warn(f"Load failed: {exc}")
                data = {}
        else:
            data = {}
        try:
            self.gap.setValue(int(data.get("gap_ms", 300)))
        except (TypeError, ValueError):
            self.gap.setValue(300)
        targets = data.get("targets") or [
            asdict(Target(target_ul=5.0, duration_ms=60.0, n_pulses=pulses_for_total(5.0))),
            asdict(Target(target_ul=10.0, duration_ms=100.0, n_pulses=pulses_for_total(10.0))),
            asdict(Target(target_ul=15.0, duration_ms=130.0, n_pulses=pulses_for_total(15.0))),
        ]
        for t in targets:
            try:
                self.add_row(Target(**t))
            except (TypeError, ValueError) as exc:
                self.warn(f"Skipped bad target {t}: {exc}")

    def closeEvent(self, event):
        self._save()
        self._disconnect()
        super().closeEvent(event)


def main():
    app = QApplication(sys.argv)
    win = CalibWindow()
    win.resize(900, 600)
    win.show()
    sys.exit(app.exec())


if __name__ == "__main__":
    main()
