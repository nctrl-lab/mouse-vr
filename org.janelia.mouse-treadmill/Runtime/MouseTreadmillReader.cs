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
        public float rollScale = 0.0220f; // Calibration scale for roll (degree / pixel)
        public float pitchScale = 0.0186f; // Calibration scale for pitch (degree / pixel)
        public float yawScale = 0.014f; // Calibaration scale for yaw (degree / pixel)
        public const float BALL_DIAMETER_INCH = 16f; // 16 inch = 40.64 cm
        public const float BALL_ARC_LENGTH_PER_DEGREE = BALL_DIAMETER_INCH * 0.254f * Mathf.PI / 360; // (0.035465 dm / degree)

        public void Start()
        {
            _ftdiReader = new FtdiReader();
            _ftdiReader.Start(); // This starts a thread that continously reads serial input
        }

        public void Update(ref Vector3 position, ref Vector3 rotationAngle, ref MouseTreadmillController.MouseTreadmillLog treadmillLog)
        {
            // dx is right(+)-left(-). dz is forward(+)-back(-). dy is rotation angle (clockwise(+)-counterclockwise(-))
            UInt64 _readTimestampMs = 0;
            int _dx = 0; int _dz = 0; int _dy = 0;
            while (GetNextMessage(ref _message)) // Reads till there's no remaining buffer
            {
                _readTimestampMs = _message.readTimestampMs;
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

                // Calculate future position and rotation
                float pitch = _dz * pitchScale;
                float roll = _dx * rollScale;
                float yaw = _dy * yawScale;

                rotationAngle.y += (allowRotation) ? yaw : 0f;
                float cos = Mathf.Cos(rotationAngle.y * Mathf.Deg2Rad);
                float sin = Mathf.Sin(rotationAngle.y * Mathf.Deg2Rad);
                float forward = pitch * BALL_ARC_LENGTH_PER_DEGREE;
                float side = roll * BALL_ARC_LENGTH_PER_DEGREE;
                position.z += forward * cos - side * sin;
                position.x += forward * sin + side * cos;

                // Log
                treadmillLog.readTimestampMs = _readTimestampMs;
                treadmillLog.ballSpeed = Mathf.Sqrt(Mathf.Pow(forward, 2) + Mathf.Pow(side, 2)) / Time.deltaTime;
                treadmillLog.pitch = pitch;
                treadmillLog.roll = roll;
                treadmillLog.yaw = yaw;
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

        private FtdiReader _ftdiReader;
        private Byte[] _ftdiReaderBuffer = new byte[FtdiReader.READ_SIZE_BYTES];

        private MouseTreadmillParser.Message _message = new MouseTreadmillParser.Message();
    }
}