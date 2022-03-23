using System;
using System.Collections.Generic;
using UnityEngine;

namespace Janelia
{
    public class MouseTreadmillReader
    {
        public bool allowMovement = true; // Move actor only when this is true
        public bool allowRotationYaw = false;
        public bool allowRotationRoll = false; // Rotation by roll instead of yaw
        public float maxRotationSpeed = 120.0f;
        public bool followPath = false;
        public bool reverseDirection = false;
        public bool logTreadmill = true;
        public float pitchScale = 0.144f; // Calibration scale for pitch (degree / pixel)
        public float rollScale = 0.170f; // Calibration scale for roll (degree / pixel)
        public float yawScale = 0.112f; // Calibaration scale for yaw (degree / pixel)
        public float forwardMultiplier = 1f;
        public float sideMultiplier = 1f;
        public const float BALL_DIAMETER_INCH = 16f; // 16 inch = 40.64 cm
        public const float BALL_ARC_LENGTH_PER_DEGREE = BALL_DIAMETER_INCH * 0.254f * Mathf.PI / 360; // (0.035465 dm / degree)

        public void Start()
        {
            _ftdiReader = new FtdiReader();
            _ftdiReader.Start(); // This starts a thread that continously reads serial input
        }

        public void Update(ref Vector3 position, ref Vector3 rotationAngle, ref MouseTreadmillLog treadmillLog)
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
                float cos = Mathf.Cos(rotationAngle.y * Mathf.Deg2Rad);
                float sin = Mathf.Sin(rotationAngle.y * Mathf.Deg2Rad);
                float forward = pitch * BALL_ARC_LENGTH_PER_DEGREE * forwardMultiplier;
                float side = roll * BALL_ARC_LENGTH_PER_DEGREE * sideMultiplier;
                position.z += forward * cos - side * sin;
                position.x += forward * sin + side * cos;

                // Both yaw and roll can affect rotation
                if (allowRotationYaw)
                {
                    rotationAngle.y += yaw;
                }

                if (followPath || allowRotationRoll)
                {
                    // rotation is governed by roll
                    // side speed (dm / s) is clipped by sigmoid function
                    rotationAngle.y += maxRotationSpeed * (2 / (1 + Mathf.Exp(-side / Time.deltaTime)) - 1);
                }

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

        public void LogParameters()
        {
            parameterLog.allowRotationYaw = allowRotationYaw;
            parameterLog.allowRotationRoll = allowRotationRoll;
            parameterLog.maxRotationSpeed = maxRotationSpeed;
            parameterLog.followPath = followPath;
            parameterLog.reverseDirection = reverseDirection;
            parameterLog.logTreadmill = logTreadmill;
            parameterLog.rollScale = rollScale;
            parameterLog.pitchScale = pitchScale;
            parameterLog.yawScale = yawScale;
            parameterLog.forwardMultiplier = forwardMultiplier;
            parameterLog.sideMultiplier = sideMultiplier;
            parameterLog.ballDiameterInch = BALL_DIAMETER_INCH;
            parameterLog.ballArcLengthPerDegree = BALL_ARC_LENGTH_PER_DEGREE;
            Logger.Log(parameterLog);
        }

        public class MouseTreadmillLog : Logger.Entry
        {
            public UInt64 readTimestampMs;
            public Vector3 position;
            public float speed;
            public float rotation;
            public float ballSpeed;
            public float pitch;
            public float roll;
            public float yaw;
            public float distance;
            public List<string> events = new List<string>();
        };

        public class MouseTreadmillParameterLog : Logger.Entry
        {
            public bool allowRotationYaw;
            public bool allowRotationRoll;
            public float maxRotationSpeed;
            public bool followPath;
            public bool reverseDirection;
            public bool logTreadmill;
            public float rollScale;
            public float pitchScale;
            public float yawScale;
            public float forwardMultiplier = 1f;
            public float sideMultiplier = 1f;
            public float ballDiameterInch;
            public float ballArcLengthPerDegree;
        }; public MouseTreadmillParameterLog parameterLog = new MouseTreadmillParameterLog(); 

        private FtdiReader _ftdiReader;
        private Byte[] _ftdiReaderBuffer = new byte[FtdiReader.READ_SIZE_BYTES];

        private MouseTreadmillParser.Message _message = new MouseTreadmillParser.Message();
    }
}