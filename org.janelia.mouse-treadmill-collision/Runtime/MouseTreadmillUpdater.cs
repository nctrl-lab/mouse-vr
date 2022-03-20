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
        public float rollScale = 0.00078f; // Calibration scale for roll (dm / pixel)
        public float pitchScale = 0.00066f; // Calibration scale for pitch (dm / pixel)
        public float yawScale = 0.014f; // Calibaration scale for yaw (degree / pixel)
        public bool enableKeyboard = false;
        public float keyboardSpeed = 2.0f;
        public float keyboardRotationSpeed = 60.0f;
        public MouseTreadmillController.MouseTreadmillLog treadmillLog = new MouseTreadmillController.MouseTreadmillLog();

        public void Start()
        {
            _reader = new MouseTreadmillReader();
            _reader.allowMovement = allowMovement;
            _reader.allowRotation = allowRotation;
            _reader.reverseDirection = reverseDirection;
            _reader.logTreadmill = logTreadmill;
            _reader.rollScale = rollScale;
            _reader.pitchScale = pitchScale;
            _reader.yawScale = yawScale;
            _reader.Start();

            LogParameters();
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

        private void LogParameters()
        {
            _currentMouseTreadmillParametersLog.MouseTreadmill_allowRotation = allowRotation;
            _currentMouseTreadmillParametersLog.MouseTreadmill_reverseDirection = reverseDirection;
            _currentMouseTreadmillParametersLog.MouseTreadmill_rollScale = rollScale;
            _currentMouseTreadmillParametersLog.MouseTreadmill_pitchScale = pitchScale;
            _currentMouseTreadmillParametersLog.MouseTreadmill_yawScale = yawScale;
            Logger.Log(_currentMouseTreadmillParametersLog);
        }

        private MouseTreadmillReader _reader;
        private Vector3 _position;
        private Vector3 _positionPrev;
        private Vector3 _rotation;
        private Vector3 _rotationPrev;
        
        // To make `Janelia.Logger.Log<T>()`'s call to JsonUtility.ToJson() work correctly,
        // the `T` must be marked `[Serlializable]`, but its individual fields need not be
        // marked `[SerializeField]`.  The individual fields must be `public`, though.

        [Serializable]
        private class MouseTreadmillParametersLog : Logger.Entry
        {
            public bool MouseTreadmill_allowRotation;
            public bool MouseTreadmill_reverseDirection;
            public float MouseTreadmill_rollScale;
            public float MouseTreadmill_pitchScale;
            public float MouseTreadmill_yawScale;
        };
        private MouseTreadmillParametersLog _currentMouseTreadmillParametersLog = new MouseTreadmillParametersLog();

    }
}