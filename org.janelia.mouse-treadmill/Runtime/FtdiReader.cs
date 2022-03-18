using System;
using System.Threading;
using UnityEngine;

using FTD2XX_NET;

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

            SetStreaming(ftdi, 1);

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
                SetStreaming(ftdi, 0);
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