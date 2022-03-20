// Include this script on gameObject to be controlled by Mouse Treadmill

using System;
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
            _position = transform.position;
            _rotation = transform.eulerAngles;
            _reader.Update(ref _position, ref _rotation);

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

        [SerializeField] private Vector3 _position, _rotation;
        private MouseTreadmillReader _reader;
    }
}