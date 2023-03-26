using System;
using System.Threading;
using UnityEngine;

using FTD2XX_NET;


/*

The system communicates using the FTDI D2xx Driver library.
The manual says Virtual COM protocol can result defective communication.
Baudrate: 1250000 bits per second

Command:
  - [252 0]: dump internal register
      - This command returns 50 bytes read from registers in each camera
  - [254 0]: stop motion data
  - [255 0]: start motion data
      - This command initiates the 4kHz motion data from the cameras.
      - Each sample is 12 bytes.

Packet design (12 bytes/packet):
  - 1: zero
  - 2: a counter from 1 to 255

  - 3: delta_x from camera 0 - zero centered on 128
  - 4: delta_y from camera 0 - zero centered on 128
  - 5: delta_x from camera 1 - zero centered on 128
  - 6: delta_y from camera 1 - zero centered on 128

  - 7: surface quality from camera 0 (higher the better / worst is 1)
  - 8: surface quality from camera 1

  - 9: high byte of shutter speed, camera 0
  - 10: low byte of shutter speed, camera 0
  - 11: high byte of shutter speed, camera 1
  - 12: low byte of shutter speed, camera 1
       --> shutterSpeed = ((HighByte -1)*256+lowByte) / 24MHz

Packet speed: 4000 packets / second
Ftdi is checked at 400 Hz

```cs
// example code
ser = SerialPort("COM3", 1250000);
ser.Open();
ser.Write(new byte[] {255, 0}, 0, 2); // this starts streaming
ser.Read(buffer, 0, read_n_byte); // read data
processData(buffer); // custom function that processes data
ser.Write(new byte[] {254, 0}, 0, 2); // this end streaming
ser.Close();
```

- Pseudocode for FtdiReader
```python
class FtdiReader:
    def Start(deviceIndex = 0)
    def Take(packets)
    def Clear()
    def OnDisable() # stops thread and close FTDI connection
```

* This uses org.janelia.io/Runtime/RingBuffer.cs RingBuffer class to store packets

- Pseudocode for RingBuffer
```python
class RingBuffer:
    def __init__(itemCount=400, itemSize=12*10)
    def Give(packets) # save 120 bytes
    def Take(packets) # take 120 bytes
    def Clear() # clear buffer
```

*/

namespace Janelia
{
    public class FtdiReader
    {

        // This reads 10 packets (=120 bytes) per read.
        public const UInt32 READ_SIZE_BYTES = 120;

        public bool Start(int deviceIndex = 0)
        {
            ftStatus = ftdi.GetNumberOfDevices(ref _deviceCount);
            if (ftStatus == FTDI.FT_STATUS.FT_OK)
            {
                Debug.Log("FtdiReader.Start: " + _deviceCount.ToString() + " device detected");
            }
            else
            {
                Debug.Log("FtdiReader.Start: failed to get number of devices (error " + ftStatus.ToString() + ")");
                return false;
            }

            // If no devices available, return
            if (_deviceCount == 0)
            {
                Debug.Log("FtdiReader.Start: failed to get number of devices (error " + ftStatus.ToString() + ")");
                return false;
            }

            // Check the device number is smaller than the device count
            if (deviceIndex < 0 || deviceIndex >= _deviceCount)
            {
                Debug.Log("FtdiReader.Start: device index is out of range");
                return false;
            }

            // Get device information
            FTDI.FT_DEVICE_INFO_NODE[] ftdiDeviceList = new FTDI.FT_DEVICE_INFO_NODE[_deviceCount];
            ftStatus = ftdi.GetDeviceList(ftdiDeviceList);

            // Open device
            ftStatus = ftdi.OpenBySerialNumber(ftdiDeviceList[deviceIndex].SerialNumber);
            if (ftStatus != FTDI.FT_STATUS.FT_OK)
            {
                Debug.Log("FtdiReader.Start: failed to open device:" + deviceIndex.ToString() + " (error " + ftStatus.ToString() + ")");
                return false;
            }

            // Set up device data parameters
            ftStatus = ftdi.SetBaudRate(1250000);
            ftStatus = ftdi.SetTimeouts(1000, 1000); // 1s timeout for read and write

            SetStreaming(ftdi, 1); // This sends a serial command to start streaming

            _thread = new Thread(ThreadFunction);
            _thread.Start();

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
            if (ftdi.IsOpen)
            {
                SetStreaming(ftdi, 0); // This tells to stop streaming
                ftStatus = ftdi.Close();
                if (ftStatus == FTDI.FT_STATUS.FT_OK)
                {
                    Debug.Log("FtdiReader.Close: device closed");
                }
                else
                {
                    Debug.Log("FtdiReader.Close: failed closing (error " + ftStatus.ToString() + ")");
                }
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
            Debug.Log("FtdiReader.ThreadFunction: starting");

            byte[] recvBuffer = new byte[READ_SIZE_BYTES];
            UInt32 numBytesRead = 0;
            UInt32 numBytesDump = 0;
            while (!_stopThread)
            {
                ftStatus = ftdi.Read(recvBuffer, READ_SIZE_BYTES, ref numBytesRead);
                if (READ_SIZE_BYTES == numBytesRead)
                {
                    // Check whether the packet is corrupted
                    if (recvBuffer[0] == 0)
                    {
                        _ringBuffer.Give(recvBuffer);
                    }
                    else
                    {
                        int i = 0;
                        while (i < READ_SIZE_BYTES && recvBuffer[i]!=0) i++;
                        if (i%12 != 0)
                            ftStatus = ftdi.Read(new byte[i%12], (UInt32)i%12, ref numBytesDump);
                        Debug.Log("FtdiReader.ThreadFunction: packet reading error");
                        _errorCount++;
                    }
                }
                else
                {
                    Debug.Log("FtdiReader.ThreadFunction: failed to read data");
                    _errorCount++;
                }

                if (_errorCount > 10)
                {
                    Debug.Log("FtdiReader.ThreadFunction: stopping due to too many read errors");
                    return;
                }
            }
            Debug.Log("FtdiReader.ThreadFunction: stopping");
            return;
        }

        // SetStreaming(ftdi, 1) will start streaming
        // SetStreaming(ftdi, 0) will stop streaming
        private void SetStreaming(FTDI ftdi, int status)
        {
            UInt32 numBytesWritten = 0;
            byte[] writeData = new byte[2];
            if (status > 0)
                writeData[0] = 255;
            else
                writeData[0] = 254;
            writeData[1] = 0;
            ftdi.Write(writeData, writeData.Length, ref numBytesWritten);
        }

        private const int BUFFER_COUNT = 400; // Discard data after 1 second

        private UInt32 _deviceCount;
        private int _errorCount;

        private Thread _thread;
        private bool _stopThread = false;

        private RingBuffer _ringBuffer = new RingBuffer(BUFFER_COUNT, (int)READ_SIZE_BYTES);

        private FTDI ftdi = new FTDI();
        private FTDI.FT_STATUS ftStatus = FTDI.FT_STATUS.FT_OK;
    }
}
