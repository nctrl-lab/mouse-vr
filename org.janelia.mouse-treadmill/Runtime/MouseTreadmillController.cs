// Include this script on gameObject to be controlled by Mouse Treadmill

using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace Janelia
{
    public class MouseTreadmillController : MonoBehaviour
    {    
        [SerializeField] private bool allowMovement = true;
        [SerializeField] private bool allowRotation = false;
        [SerializeField] private bool reverseDirection = false;
        [SerializeField] private bool logTreadmill = false;
        [SerializeField] private float rollScale = 0.00078f; // Calibration scale for roll (dm / pixel)
        [SerializeField] private float pitchScale = 0.00066f; // Calibration scale for pitch (dm / pixel)
        [SerializeField] private float yawScale = 0.014f; // Calibaration scale for yaw (degree / pixel)
        [SerializeField] private bool enableKeyboard = false;
        [SerializeField] private float keyboardSpeed = 2.0f;
        [SerializeField] private float keyboardRotationSpeed = 60.0f;
        public MouseTreadmillLog treadmillLog = new MouseTreadmillLog();

        private void Start()
        {
            _reader = new MouseTreadmillReader();
            _reader.Start();
        }

        private void Update()
        {
            // Keep updating parameters
            _reader.allowMovement = allowMovement;
            _reader.allowRotation = allowRotation;
            _reader.reverseDirection = reverseDirection;
            _reader.logTreadmill = logTreadmill;
            _reader.rollScale = rollScale;
            _reader.pitchScale = pitchScale;
            _reader.yawScale = yawScale;

            // Read data and update
            _prev_position = _position;
            _prev_rotation = _rotation;
            _position = transform.position;
            _rotation = transform.eulerAngles;

            treadmillLog.position = _position;
            treadmillLog.speed = Vector3.Distance(_position, _prev_position) / Time.deltaTime;
            treadmillLog.rotation = _rotation.y;

            _reader.Update(ref _position, ref _rotation, ref treadmillLog);


            if (enableKeyboard)
            {
                if (Input.GetKey("q") || Input.GetKey(KeyCode.Escape))
                {
                    Quit();
                }

                float cos = Mathf.Cos(_rotation.y * Mathf.Deg2Rad);
                float sin = Mathf.Sin(_rotation.y * Mathf.Deg2Rad);
                float forward = Input.GetAxis("Vertical") * Time.deltaTime;
                float side = Input.GetAxis("Horizontal") * Time.deltaTime;
                if (allowRotation)
                {
                    _position.z += forward * cos * keyboardSpeed;
                    _position.x += forward * sin * keyboardSpeed;
                    _rotation.y += side * keyboardRotationSpeed;
                }
                else
                {
                    _position.z += (forward * cos - side * sin) * keyboardSpeed;
                    _position.x += (forward * sin + side * cos) * keyboardSpeed;
                }
            } 

            // Update position
            transform.position = _position;
            transform.eulerAngles = _rotation;

            // Log
            if (logTreadmill)
            {
                Logger.Log(treadmillLog);
            }

            treadmillLog.events.Clear();
        }

        private void OnTriggerEnter(Collider other) // This comes before Update() method
        {
            Debug.Log(other.name);
            string[] subnames = other.name.Trim('_').Split('_');
            if (subnames.Length==2 && subnames[1].Contains('r'))
            {
                treadmillLog.events.Add(subnames[0]);
            }
        }

        private void OnDisable()
        {
            _reader.OnDisable();
        }

        private void Quit()
        {
            #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
            #elif UNITY_STANDALONE
                Application.Quit();
            #endif
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
            public List<string> events = new List<string>();
        }; 

        private Vector3 _position, _prev_position, _rotation, _prev_rotation;
        private MouseTreadmillReader _reader;
    }
}