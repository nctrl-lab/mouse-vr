using System;
using System.Collections.Generic;
using UnityEngine;

namespace Janelia
{
    public class MouseTreadmillReader
    {
        public bool allowMovement = true; // Move actor only when this is true
        public bool allowRotation = false;
        public bool reverseDirection = false;
        public bool logTreadmill = true;
        public float rollScale = 0.00078f; // Calibration scale for roll (dm / pixel)
        public float pitchScale = 0.00066f; // Calibration scale for pitch (dm / pixel)
        public float yawScale = 0.014f; // Calibaration scale for yaw (degree / pixel)

        public void Start()
        {
            _ftdiReader = new FtdiReader();
            _ftdiReader.Start(); // This starts a thread that continously reads serial input
        }

        public void Update(ref Vector3 position, ref Vector3 rotationAngle)
        {
            // dx is right(+)-left(-). dz is forward(+)-back(-). dy is rotation angle (clockwise(+)-counterclockwise(-))
            int _dx = 0; int _dz = 0; int _dy = 0;
            while (GetNextMessage(ref _message)) // Reads till there's no remaining buffer
            {
                _dx = _message.y0 - _message.y1;
                _dz = _message.y0 + _message.y1;
                _dy = _message.x0 + _message.x1; // Unity has a left-handed coordinate system.

            }


            // Translation and rotation logic comes here
            if (allowMovement)
            {
                if (reverseDirection)
                {
                    _dx = -_dx;
                    _dy = -_dy;
                    _dz = -_dz;
                }

                // Log position before the ball movement
                _treadmillLog.position = position;
                _treadmillLog.rotation = rotationAngle.y;
                _treadmillLog.pitch = _dz;
                _treadmillLog.roll = _dx;
                _treadmillLog.yaw = _dy;

                // Calculate future position and rotation
                rotationAngle.y += (allowRotation) ? _dy * yawScale : 0f;
                float cos = Mathf.Cos(rotationAngle.y * Mathf.Deg2Rad);
                float sin = Mathf.Sin(rotationAngle.y * Mathf.Deg2Rad);
                float forward = _dz * pitchScale;
                float side = _dx * rollScale;
                position.z += forward * cos - side * sin;
                position.x += forward * sin + side * cos;

                // Log future position (it might not be there in case of collision)
                _treadmillLog.next_position = position;
                _treadmillLog.next_rotation = rotationAngle.y;

                // Logging is done only when the gameObject is allowed to move
                if (logTreadmill)
                {
                    Logger.Log(_treadmillLog);
                }
            }
        }

        public bool GetNextMessage(ref MouseTreadmillParser.Message _message)
        {
            long _timestampMs = 0;
            if (_ftdiReader.Take(ref _ftdiReaderBuffer, ref _timestampMs)) // Read 10 packets from ftdiReader buffer
            {
                return MouseTreadmillParser.ParseMessage(ref _message, _ftdiReaderBuffer, _timestampMs); // Merge 10 packets to get the sum of dx0, dy0, dx1, and dy1
            }
            return false;
        }

        public void OnDisable()
        {
            _ftdiReader.OnDisable(); // Disconnect FTDI device
        }

        [Serializable]
        private class MouseTreadmillLog : Logger.Entry
        {
            public Vector3 position;
            public Vector3 next_position;
            public float rotation;
            public float next_rotation;
            public float pitch;
            public float roll;
            public float yaw;
        }
        private MouseTreadmillLog _treadmillLog = new MouseTreadmillLog();

        private FtdiReader _ftdiReader;
        private Byte[] _ftdiReaderBuffer = new byte[FtdiReader.READ_SIZE_BYTES];

        private MouseTreadmillParser.Message _message = new MouseTreadmillParser.Message();
    }
}