// This is all-in-one script to test ball camera sensor and to calibrate the moving distance 
// Roll the ball several times and measure moved distance
// 
// We want to convert raw pixel units from camera to degree angles.
// pitchScale and rollScale unit is degree / pixel
//
// For example, if you rolled (moved forward) three times and the Z position was 60,
// pitchScale will be 3/60 = 0.05

using System;
using System.Threading;
using UnityEngine;

using FTD2XX_NET;

namespace Janelia
{
    public class BallTester : MonoBehaviour
    {
        // Ball related variables
        public float pitchScale = 1f; // Calibration scale for pitch (degree / pixel)
        public float rollScale = 1f; // Calibration scale for roll (degree / pixel)
        public const float BALL_DIAMETER_INCH = 16f; // 16 inch = 40.64 cm
        public const float BALL_ARC_LENGTH_PER_DEGREE = BALL_DIAMETER_INCH * 0.254f * Mathf.PI / 360; // (0.035465 dm / degree)
        [SerializeField] private int y0 = 0; // camera 0 
        [SerializeField] private int y1 = 0; // camera 1
        [SerializeField] private int dz = 0; // forward movement
        [SerializeField] private int dx = 0; // side movement
        [SerializeField] private int readCount = 0;
        [SerializeField] private int errorCount = 0;

        // One packet is 12 bytes. We will read 10 packets at a time.
        // Ball cameras are running at 4 kHz, so we are reading at 400 Hz.
        private const UInt32 READ_SIZE_BYTES = 120;
        private byte[] _buffer = new byte[READ_SIZE_BYTES];

        // FTDI related variables
        private FTDI ftdi = new FTDI();
        private FTDI.FT_STATUS ftStatus = FTDI.FT_STATUS.FT_OK;
        private UInt32 numBytesWritten = 0;
        private UInt32 numBytesRead = 0;

        // Thread function
        private Thread _thread;
        private bool _stopThread = false;

        private Rigidbody _rigidbody;
        private Collider _collider;

        // Check physics settings
        void Awake()
        {
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
            _rigidbody.constraints |= RigidbodyConstraints.FreezeRotationY;

            // Check collider: set friction to zero
            _collider = GetComponent<Collider>();
            PhysicMaterial material = new PhysicMaterial();
            material.dynamicFriction = 0;
            material.staticFriction = 0;
            material.frictionCombine = PhysicMaterialCombine.Minimum;
            _collider.material = material;
        }

        void Start()
        {
            // Open ftdi
            ftdi.OpenByIndex(0);
            ftdi.SetBaudRate(1250000);
            ftdi.SetTimeouts(1000, 1000);
            ftdi.SetLatency(0);
            ftdi.Write(new byte[] { 255, 0 }, 2, ref numBytesWritten);
            Debug.Log("FTDI open");

            // Start thread
            _thread = new Thread(ThreadFunction);
            _thread.Start();
        }

        void Update()
        {
            float forward = dz * pitchScale * BALL_ARC_LENGTH_PER_DEGREE;
            float side = dx * rollScale * BALL_ARC_LENGTH_PER_DEGREE;
            _rigidbody.velocity = (new Vector3(side, 0f, forward)) / Time.deltaTime;
            dx = 0;
            dz = 0;
        }

        void OnDisable()
        {
            _stopThread = true;
            if (ftdi.IsOpen)
            {
                ftdi.Write(new byte[] { 254, 0 }, 2, ref numBytesWritten);
                ftdi.Close();
                Debug.Log("FTDI closed");
            }
            if (_thread != null) {
                _thread.Abort();
                Debug.Log("Thread aborted");
            }
        }

        private void ThreadFunction()
        {
            Debug.Log("Thread started");
            while (!_stopThread)
            {
                ftdi.Read(_buffer, READ_SIZE_BYTES, ref numBytesRead);
                if (READ_SIZE_BYTES == numBytesRead)
                {
                    // parse message
                    int i = 0;
                    y0 = 0;
                    y1 = 0;
                    while (i < READ_SIZE_BYTES - 5)
                    {
                        if (_buffer[i] == 0)
                        {
                            y0 += (int)_buffer[i + 3] - 128;
                            y1 += (int)_buffer[i + 5] - 128;
                            i += 12;
                            readCount++;
                        }
                        else
                        {
                            while (i < READ_SIZE_BYTES && _buffer[i] != 0) i++;
                            errorCount++;
                        }
                    }
                    dz += y0 + y1;
                    dx += y0 - y1;
                    // parse message end
                }
            }
            Debug.Log("Thread finished");
        }
    }
}