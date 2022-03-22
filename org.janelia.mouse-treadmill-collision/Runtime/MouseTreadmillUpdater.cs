using System;
using System.Collections.Generic;
using UnityEngine;

namespace Janelia
{
    // Gets Mouse Treadmill input and converts it to kinematic motion, for use by a subclass
    // of `Janelia.KinematicSubject`, from the org.janelia.collision-handling package.
    // The direction for forward motion is the positive local Z axis.
    // The up direction is the positive local Y axis.

    public class MouseTreadmillUpdater : KinematicSubject.IKinematicUpdater
    {    
        public bool allowMovement = true;
        public bool allowRotation = false;
        public bool reverseDirection = false;
        public bool logTreadmill = true;
        public float pitchScale = 0.144f; // Calibration scale for pitch (degree / pixel)
        public float rollScale = 0.170f; // Calibration scale for roll (degree / pixel)
        public float yawScale = 0.014f; // Calibaration scale for yaw (degree / pixel)
        public float forwardMultiplier = 1f;
        public float sideMultiplier = 1f;
        public bool enableKeyboard = false;
        public float keyboardSpeed = 2.0f;
        public float keyboardRotationSpeed = 90.0f;
        public MouseTreadmillReader.MouseTreadmillLog treadmillLog = new MouseTreadmillReader.MouseTreadmillLog();

        public void Start()
        {
            _reader = new MouseTreadmillReader();
            _reader.allowMovement = allowMovement;
            _reader.allowRotation = allowRotation;
            _reader.reverseDirection = reverseDirection;
            _reader.logTreadmill = logTreadmill;
            _reader.pitchScale = pitchScale;
            _reader.rollScale = rollScale;
            _reader.yawScale = yawScale;
            _reader.forwardMultiplier = forwardMultiplier;
            _reader.sideMultiplier = sideMultiplier;
            _reader.LogParameters();
            _reader.Start();
        }

        public void Update()
        {
            _reader.allowMovement = allowMovement;
            _reader.allowRotation = allowRotation;

            _positionPrev = _position;
            _rotationPrev = _rotation;


            if (enableKeyboard)
            {
                float forward = Input.GetAxis("Vertical") * Time.deltaTime;
                float side = Input.GetAxis("Horizontal") * Time.deltaTime;
                
                _position.z += forward * keyboardSpeed;
                if (allowRotation)
                {
                    _rotation.y += side * keyboardRotationSpeed;
                }
                else
                {
                    _position.x += side * keyboardSpeed;
                }
            } 
            _reader.Update(ref _position, ref _rotation, ref treadmillLog);

            treadmillLog.position = _position;
            treadmillLog.speed = Vector3.Distance(_position, _positionPrev) / Time.deltaTime;
            treadmillLog.rotation = _rotation.y;
            if (logTreadmill)
            {
                Logger.Log(treadmillLog);
            }
            treadmillLog.events.Clear();

        }

        public Vector3? Translation()
        {
            if (_position == _positionPrev)
                return null;
            return _position - _positionPrev;
        }

        public Vector3? RotationDegrees()
        {
            if (_rotation == _rotationPrev)
                return null;
            return _rotation - _rotationPrev;
        }

        // Not part of the standard `KinematicSubject.IKinematicUpdater`.
        public void OnDisable()
        {
            _reader.OnDisable();
        }

        private MouseTreadmillReader _reader;
        private Vector3 _position, _positionPrev, _rotation, _rotationPrev;
    }
}