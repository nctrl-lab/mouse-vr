using System;
using System.Threading;
using System.IO.Ports;
using UnityEngine;

// This connects to Ball camera through Serial communication.
// Baud rate is 1250000 bps, but the data stream seems faster than that (6 byte/packet * 41500 packet/s * 8 bit/byte = 2 Mbps)
// Packet size is 6 bytes.
// packet[0] = 0 : marks start of data pack - no other data can be 0
// packet[1] = counter (1 to 255)
// packet[2] : camera 0 x pixel shift, offset by 128
// packet[3] : camera 0 y pixel shift, offset by 128
// packet[4] : camera 1 x pixel shift, offset by 128
// packet[5] : camera 1 y pixel shift, offset by 128
//
// Sampling frequency was around 41.5 kHz
// Teensy is sending 341-342 packets (2046-2052 bytes)in bulk, which is around 1.2 kHz.
//
//  Actual calculation of pitch, roll, and yaw is done as below:
//      dx0 += x[0] - 128, dx1 += x[1] - 128        : accumulate movement
//      dy0 += y[0] - 128, dy1 += y[1] - 128 
//      pitch = (dy0 + dy1) / sqrt(2)
//      roll = (dy0 - dy1) / sqrt(2)
//      yaw = (dx0 + dx1) / 2
//
//  Because pixel size can be arbitrary, we should calibrate the actual distance
//
// Dohoung Kim
// 20230421
// Albert Lee Lab, Janelia, HHMI

namespace Janelia
{
    public class PixartReader
    {
        // This reads 10 packets (=60 bytes / 400 Hz) per read.
        public const int PACKET_SIZE = 6; // 12 for FTDI, 6 for PixArt ball reader
        public const int READ_SIZE_BYTES = 60;

        public string comPort = "COM3";
        public PixartReader(string comPortPixart)
        {
            comPort = comPortPixart;
        }

        public bool Start()
        {
            // Set up device data parameters
            _serial = new SerialPort();
            _serial.PortName = comPort;
            _serial.BaudRate = 1250000;
            _serial.Parity = Parity.None;
            _serial.DataBits = 8;
            _serial.StopBits = StopBits.One;
            _serial.Handshake = Handshake.None;
            _serial.RtsEnable = true;
            _serial.DtrEnable = true;

            // Try to open serial port
            // If it fails just ignore it
            try
            {
                _serial.Open();
                _serial.DiscardInBuffer();
                _serial.DiscardOutBuffer();
                _serial.BaseStream.Flush();
                
                SetStreaming(_serial, 1); // This sends a serial command to start streaming

                _thread = new Thread(ThreadFunction);
                _thread.Start();
            }
            catch
            {
                Debug.Log("PixartRedaer: " + comPort + " is not available");
            }

            return true;
        }

        public bool Take(ref Byte[] taken, ref long timestampMs)
        {
            return _ringBuffer.Take(ref taken, ref timestampMs);
        } 

        public void Clear()
        {
            _ringBuffer.Clear();
        }

        public void Close()
        {
            _stopThread = true;
            if (_serial.IsOpen)
            {
                SetStreaming(_serial, 0); // This tells to stop streaming
                _serial.Close();
            }
        }

        public void OnDisable() {
            Close();
            if (_thread != null)
            {
                _thread.Abort();
            }
        }

        private void ThreadFunction()
        {
            Debug.Log("SerialReader.ThreadFunction: starting");

            byte[] recvBuffer = new byte[READ_SIZE_BYTES];
            while (!_stopThread)
            {
                if (_serial.BytesToRead >= READ_SIZE_BYTES)
                {
                    _serial.Read(recvBuffer, 0, READ_SIZE_BYTES);

                    // Check whether the packet is corrupted
                    if (recvBuffer[0] == 0)
                    {
                        _ringBuffer.Give(recvBuffer);
                    }
                    else
                    {
                        int i = 0;
                        while (i < READ_SIZE_BYTES && recvBuffer[i]!=0) i++;
                        if (i%6 != 0)
                            _serial.Read(new byte[i%6], 0, i%6);
                        Debug.Log("SerialReader.ThreadFunction: packet reading error");
                        _errorCount++;
                    }
                }

                if (_errorCount > 10)
                {
                    Debug.Log("SerialReader.ThreadFunction: stopping due to too many read errors");
                    return;
                }
            }
            Debug.Log("SerialReader.ThreadFunction: stopping");
            return;
        }

        // SetStreaming(serial, 1) will start streaming
        // SetStreaming(serial), 0) will stop streaming
        private void SetStreaming(SerialPort serial, int status)
        {
            if (status > 0)
                serial.Write(new byte[] { 255, 0 }, 0, 2);
            else
                serial.Write(new byte[] { 254, 0 }, 0, 2);
        }

        private const int BUFFER_COUNT = 400; // Discard data after 1 second
        private int _errorCount;

        private Thread _thread;
        private bool _stopThread = false;

        private RingBuffer _ringBuffer = new RingBuffer(BUFFER_COUNT, (int)READ_SIZE_BYTES);

        private static SerialPort _serial;

    }
}
