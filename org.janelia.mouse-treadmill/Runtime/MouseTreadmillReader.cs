using System;
using System.Collections.Generic;
using UnityEngine;

namespace Janelia
{
    public class MouseTreadmillReader : MonoBehaviour
    {
        public bool isConnected = false; // Move actor only when this is true
        public float rollScale = 0.00078f; // Calibration scale for roll (dm / pixel)
        public float pitchScale = 0.00066f; // Calibration scale for pitch (dm / pixel)
        [SerializeField] private int _dx, _dz; // dx is right(+)-left(-). dz is forward(+)-back(-).

        private void Start()
        {
            _ftdiReader = new FtdiReader();
            _ftdiReader.Start(); // This starts a thread that continously reads serial input
        }

        private void Update()
        {
            _dx = 0; _dz = 0;
            while (GetNextMessage(ref _message)) // Reads till there's no remaining buffer
            {
                _dx = _message.y0 - _message.y1;
                _dz = _message.y0 + _message.y1;
            }

            if (isConnected)
            {
                transform.Translate(_dx*rollScale, 0f, _dz*pitchScale); // Move self when isConnected is true
            }
        }

        private bool GetNextMessage(ref MouseTreadmillParser.Message _message)
        {
            long _timestampMs = 0;
            if (_ftdiReader.Take(ref _ftdiReaderBuffer, ref _timestampMs)) // Read 10 packets from ftdiReader buffer
            {
                return MouseTreadmillParser.ParseMessage(ref _message, _ftdiReaderBuffer, _timestampMs); // Merge 10 packets to get the sum of dx0, dy0, dx1, and dy1
            }
            return false;
        }

        private void OnDisable()
        {
            _ftdiReader.OnDisable(); // Disconnect FTDI device
        }

        private FtdiReader _ftdiReader;
        private Byte[] _ftdiReaderBuffer = new byte[FtdiReader.READ_SIZE_BYTES];

        private MouseTreadmillParser.Message _message = new MouseTreadmillParser.Message();
    }
}
