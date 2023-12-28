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
        public bool allowRotationYaw = false; // allow rotation by yaw
        public bool allowRotationRoll = false; // allow rotation by roll
        public float maxRotationSpeed = 360.0f; // 360 degree per second
        // public bool followPath = false;
        // public float pathRotationMix = 0.25f;
        // public PathCreation.PathCreator pathCreator;
        public bool reverseDirection = false; // true if the camera is rotated at 180 degrees
        public bool logTreadmill = true; // save log of player position and velocity
        public float pitchScale = 0.144f; // Calibration scale for pitch (degree / pixel)
        public float rollScale = 0.170f; // Calibration scale for roll (degree / pixel)
        public float yawScale = 0.112f; // Calibaration scale for yaw (degree / pixel)
        public float forwardMultiplier = 1f;
        public float sideMultiplier = 1f;
        public bool enableKeyboard = false;
        public float keyboardSpeed = 3.0f; // 30 cm per second
        public string comPortPixArt = "COM3";

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
            if (!(allowRotationYaw || allowRotationRoll))
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
            _reader.comPortPixArt = comPortPixArt;
            _reader.allowMovement = allowMovement;
            _reader.allowRotationYaw = allowRotationYaw;
            _reader.allowRotationRoll = allowRotationRoll;
            _reader.maxRotationSpeed = maxRotationSpeed;
            // _reader.followPath = followPath;
            _reader.reverseDirection = reverseDirection;
            _reader.logTreadmill = logTreadmill;
            _reader.pitchScale = pitchScale;
            _reader.rollScale = rollScale;
            _reader.yawScale = yawScale;
            _reader.forwardMultiplier = forwardMultiplier;
            _reader.sideMultiplier = sideMultiplier;
            _reader.LogParameters(); // remember to log parameters every time you change
            _reader.Start();

            // Socket communication
            _socket = new SocketReader();
            _socket.Start();
        }

        // Don't forget to add ForceRenderRate.cs!!!
        // socket and log can generate errors
        private void Update()
        {
            // if (Input.GetKey("q") || Input.GetKey(KeyCode.Escape))
            // {
            //     Quit();
            // }

            // Keep updating parameters
            _reader.allowMovement = allowMovement;
            _reader.forwardMultiplier = forwardMultiplier;
            _reader.sideMultiplier = sideMultiplier;

            // Read data and update
            _positionPrev = _position; // T-2
            _rotationPrev = _rotation;
            _position = transform.position; // T-1
            _rotation = transform.eulerAngles;
            // if (!followPath)
            _deltaDistance = Vector3.Distance(_position, _positionPrev); // T-1 - T-2

            treadmillLog.position = _position; // T-1
            treadmillLog.rotation = _rotation.y;
            treadmillLog.distance += _deltaDistance;
            treadmillLog.speed = _deltaDistance / Time.deltaTime;

            _positionPrev = _position; // T-1
            _rotationPrev = _rotation;
            _reader.Update(ref _position, ref _rotation, ref treadmillLog); // T-0

            if (enableKeyboard)
            {
                float cos = Mathf.Cos(_rotation.y * Mathf.Deg2Rad);
                float sin = Mathf.Sin(_rotation.y * Mathf.Deg2Rad);
                float forward = Input.GetAxis("Vertical") * Time.deltaTime;
                float side = Input.GetAxis("Horizontal") * Time.deltaTime;
                if (allowRotationYaw || allowRotationRoll)
                {
                    _position.z += forward * cos * keyboardSpeed;
                    _position.x += forward * sin * keyboardSpeed;
                    _rotation.y += side * maxRotationSpeed;
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

            _rigidbody.MovePosition(_position); // use this if there will be no collision
            //_rigidbody.velocity = (_position - _positionPrev) / Time.deltaTime; // this works!!!
            
            if (allowRotationYaw || allowRotationRoll)
                transform.Rotate(_rotation - _rotationPrev);

            _distance += treadmillLog.pitch * MouseTreadmillReader.BALL_ARC_LENGTH_PER_DEGREE * forwardMultiplier * Mathf.Cos(_rotation.y - _rotationPrev.y);

            // if (!followPath || pathCreator == null)
            // {
                // _rigidbody.velocity = (_position - _positionPrev) / Time.deltaTime; // this works!!!
                // if (allowRotationYaw || allowRotationRoll)
                //     transform.Rotate(_rotation - _rotationPrev);
            // }
            // else
            // {
            //     float pathRotation = pathCreator.path.GetRotationAtDistance(_distance).eulerAngles.y;
            //     _rotation.y = pathRotation * pathRotationMix + _rotation.y * (1 - pathRotationMix);
            //     transform.Rotate(_rotation - _rotationPrev);

            //     _distance += treadmillLog.pitch * MouseTreadmillReader.BALL_ARC_LENGTH_PER_DEGREE * forwardMultiplier * Mathf.Cos(_rotation.y - _rotationPrev.y);
            //     transform.position = pathCreator.path.GetPointAtDistance(_distance);
            // }

            // Log
            if (logTreadmill)
            {
                Logger.Log(treadmillLog);
                if (_socket.Connected)
                    _socket.Write(ToJovianLog(treadmillLog));
            }
            treadmillLog.events.Clear();
        }

        private Byte[] ToJovianLog(MouseTreadmillReader.MouseTreadmillLog log)
        {
            float jovianRotation = Quaternion.Angle(Quaternion.Euler(0f, log.rotation, 0f), Quaternion.Euler(0f, 90f, 0f)); // this gives an absolute angle > 0

            // 45 cm = 4.5 UnityUnit = 4500 in Jovian log
            string output = String.Format("{0},{1:F0},{2:F0},{3:F0},{4:F0},{5:F0},2,{6:F0},{7:F0},{8:F0},{9:F0},{10}",
                log.readTimestampMs,
                1000 * log.position.x, 1000 * log.position.z, 1000 * log.position.y,
                1000 * log.speed, 100 * jovianRotation,
                1000 * log.ballSpeed,
                100 * log.roll, 100 * log.pitch, 100 * log.yaw,
                log.events.Count);
            if (log.events.Count > 0)
                output += $",{String.Join(",", log.events.ToArray())}";
            output += "\n";
            // To bytes
            return System.Text.Encoding.UTF8.GetBytes(output);
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
        private float _distance = 0, _deltaDistance;
        private MouseTreadmillReader _reader;
        private Rigidbody _rigidbody;
        private MouseTreadmillReader.MouseTreadmillLog treadmillLog = new MouseTreadmillReader.MouseTreadmillLog();
        public SocketReader _socket;
    }
}
