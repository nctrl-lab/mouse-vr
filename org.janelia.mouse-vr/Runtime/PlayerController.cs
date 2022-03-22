// Include this script on gameObject to be controlled by Mouse Treadmill

using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace Janelia
{
    public class PlayerController : MonoBehaviour
    {    
        public bool allowMovement = false;
        public bool allowRotation = false;
        public bool reverseDirection = false;
        public bool logTreadmill = true;
        public float pitchScale = 0.144f; // Calibration scale for pitch (degree / pixel)
        public float rollScale = 0.170f; // Calibration scale for roll (degree / pixel)
        public float yawScale = 0.112f; // Calibaration scale for yaw (degree / pixel)
        public float forwardMultiplier = 1f;
        public float sideMultiplier = 1f;
        public bool enableKeyboard = false;
        public float keyboardSpeed = 2.0f; // 20 cm per second
        public float keyboardRotationSpeed = 90.0f; // 90 degree per second

        // Check physics setting is correct
        private void Awake()
        {
            // Check rigid body exists
            _rigidbody = GetComponent<Rigidbody>();
            if (_rigidbody == null)
            {
                _rigidbody = gameObject.AddComponent<Rigidbody>();
            }
            _rigidbody.useGravity = false;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous; // Set this to Continuous Dynamic if the object speed will be higher.
            _rigidbody.constraints |= RigidbodyConstraints.FreezePositionY;
            _rigidbody.constraints |= RigidbodyConstraints.FreezeRotationX;
            _rigidbody.constraints |= RigidbodyConstraints.FreezeRotationZ;
            if (!allowRotation)
            {
                _rigidbody.constraints |= RigidbodyConstraints.FreezeRotationY;
            }
            
            // Check collider: set friction to zero
            Collider collider = GetComponent<Collider>();
            PhysicMaterial material = new PhysicMaterial();
            material.dynamicFriction = 0;
            material.staticFriction = 0;
            material.frictionCombine = PhysicMaterialCombine.Minimum;
            collider.material = material;

            // Make sure the collider of camera screen is off
            Collider[] colliderScreens = GetComponentsInChildren<Collider>();
            foreach (Collider colliderScreen in colliderScreens)
            {
                if (colliderScreen.transform.name != "Player")
                {
                    colliderScreen.enabled = false;
                }
            }
            
        }

        private void Start()
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
            _reader.LogParameters(); // remember to log parameters every time you change
            _reader.Start();
        }

        private void Update()
        {
            if (Input.GetKey("q") || Input.GetKey(KeyCode.Escape))
            {
                Quit();
            }

            // Keep updating parameters
            _reader.allowMovement = allowMovement;
            _reader.forwardMultiplier = forwardMultiplier;
            _reader.sideMultiplier = sideMultiplier;

            // Read data and update
            _positionPrev = _position; // T-2
            _rotationPrev = _rotation;
            _position = transform.position; // T-1
            _rotation = transform.eulerAngles;

            treadmillLog.position = _position; // T-1
            treadmillLog.rotation = _rotation.y;
            treadmillLog.speed = Vector3.Distance(_position, _positionPrev) / Time.deltaTime;

            _positionPrev = _position; // T-1
            _rotationPrev = _rotation;
            _reader.Update(ref _position, ref _rotation, ref treadmillLog); // T-0

            if (enableKeyboard)
            {
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

            // ***Update position***
            //      Even with colliders, when the object speed is too fast, penatration still happens.
            //      1) Make sure you have environment with thick walls.
            //      2) ColliderDectionMode should be countinous or continuous dynamic (it is more expensive than discrete).
            //      3) Add PhysicMaterial to set up friction.
            //      4) Use Rigidbody.velocity, instead of transform.Translate or rigidbody.MovePosition.
            //transform.Translate(_position - _positionPrev); // definitely ignores...
            //_rigidbody.MovePosition(_position); // ignores physics
            _rigidbody.velocity = (_position - _positionPrev) / Time.deltaTime; // this works!!!
            if (allowRotation)
                transform.Rotate(_rotation);

            // Log
            if (logTreadmill)
            {
                Logger.Log(treadmillLog);
            }
            treadmillLog.events.Clear();
        }

        private void OnTriggerEnter(Collider other) // This comes before Update() method
        {
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

        private Vector3 _position, _positionPrev, _rotation, _rotationPrev;
        private MouseTreadmillReader _reader;
        private Rigidbody _rigidbody;
        private MouseTreadmillReader.MouseTreadmillLog treadmillLog = new MouseTreadmillReader.MouseTreadmillLog();
    }
}