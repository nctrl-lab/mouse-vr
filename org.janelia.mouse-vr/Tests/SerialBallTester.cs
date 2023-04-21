// This is all-in-one script to test ball camera sensor and to calibrate the moving distance 
// Roll the ball several times and measure moved distance
//
// We want to convert raw pixel units from camera to degree angles.
// pitchScale and rollScale unit is degree / pixel
//
// For example, if you rolled (moved forward) three times and the Z position was 60,
// pitchScale will be 3/60 = 0.05
//
//
// This connects to Pixart Ball camera with teensy 4.0 through Serial communication.
// Baud rate is 1250000 bps, but the data stream seems faster than that (6 byte/packet * 41500 packet/s * 8 bit/byte = 2 Mbps)
// Packet size is 6 bytes.
// packet[0] = 0 : marks start of data pack - no other data can be 0
// packet[1] = counter (1 to 255)
// packet[2] : camera 0 x pixel shift, offset by 128
// packet[3] : camera 0 y pixel shift, offset by 128
// packet[4] : camera 1 x pixel shift, offset by 128
// packet[5] : camera 1 y pixel shift, offset by 128
//
// Sampling frequency was around 41.5 kHz
// Teensy is sending 341-342 packets in bulk, which is around 1.2 kHz.
//
//  Actual calculation of pitch, roll, and yaw is done as below:
//      dx0 += x[0] - 128, dx1 += x[1] - 128        : accumulate movement
//      dy0 += y[0] - 128, dy1 += y[1] - 128 
//      pitch = (dy0 + dy1) / sqrt(2)
//      roll = (dy0 - dy1) / sqrt(2)
//      yaw = (dx0 + dx1) / 2
//
//  Because pixel size can be arbitrary, we should calibrate the actual distance
//
// Dohoung Kim
// 20230421
// Albert Lee Lab, Janelia, HHMI

using System.Threading;
using System.IO.Ports;
using UnityEngine;

namespace Janelia
{
    public class SerialBallTester : MonoBehaviour
    {
        // Serial connection
        public static SerialPort _serial;
        public string comPort = "COM4";

        // Ball related variables
        public float pitchScale = 1f; // Calibration scale for pitch (degree / pixel)
        public float rollScale = 1f; // Calibration scale for roll (degree / pixel)
        public const float BALL_DIAMETER_INCH = 16f; // 16 inch = 40.64 cm
        public const float BALL_ARC_LENGTH_PER_DEGREE = BALL_DIAMETER_INCH * 0.254f * Mathf.PI / 360; // (0.035465 dm / degree)

        
        [SerializeField] private bool reversed = true; // camera 0 
        [SerializeField] private int x0 = 0; // camera 0 
        [SerializeField] private int y0 = 0; // camera 0 
        [SerializeField] private int x1 = 0; // camera 1
        [SerializeField] private int y1 = 0; // camera 1
        [SerializeField] private int x0_cum = 0; // camera 0 
        [SerializeField] private int y0_cum = 0; // camera 0 
        [SerializeField] private int x1_cum = 0; // camera 1
        [SerializeField] private int y1_cum = 0; // camera 1
        [SerializeField] private int dz = 0; // forward movement
        [SerializeField] private int dx = 0; // side movement
        [SerializeField] private int readCount = 0;
        [SerializeField] private int errorCount = 0;
        [SerializeField] private int missingPacket = 0;
        [SerializeField] private int nReadBytes = 0;
        [SerializeField] private int packetNum = 0;


        // One packet is 6 bytes.
        // Ball cameras are running at 4 kHz, so we are reading at 400 Hz.
        private const int PACKET_SIZE = 6;
        private byte[] _buffer;


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
            // Open serial
            _serial = new SerialPort();
            _serial.PortName = comPort;
            _serial.BaudRate = 1250000;
            _serial.Parity = Parity.None;
            _serial.DataBits = 8;
            _serial.StopBits = StopBits.One;
            _serial.Handshake = Handshake.None;
            _serial.RtsEnable = true;
            _serial.DtrEnable = true;

            _serial.Open();
            _serial.DiscardInBuffer();
            _serial.DiscardOutBuffer();
            _serial.BaseStream.Flush();
            _serial.Write(new byte[] { 255, 0 }, 0, 2);
            Debug.Log("Serial is open");

            // Start thread
            _thread = new Thread(ThreadFunction);
            _thread.Start();
        }

        void Update()
        {
            float forward = dz * pitchScale / 360;
            float side = dx * rollScale / 360;
            if (reversed) {
                forward = - forward;
                side = - side;
            }
            transform.Translate(side, 0f, forward);
            //_rigidbody.velocity = (new Vector3(side, 0f, forward)) / Time.deltaTime;
            dx = 0;
            dz = 0;

            // Timer to check packet number
            if (Time.time > 10)
            {
                Debug.Log(readCount);
                UnityEditor.EditorApplication.isPlaying = false;
            }

        }

        void OnDisable()
        {
            _stopThread = true;
            if (_serial.IsOpen)
            {
                _serial.Write(new byte[] { 254, 0 }, 0, 2);
                _serial.Close();
                Debug.Log("Serial is closed");
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
                if (_serial.BytesToRead >= PACKET_SIZE)
                {
                    nReadBytes = PACKET_SIZE * (_serial.BytesToRead / PACKET_SIZE);
                    _buffer = new byte[nReadBytes];

                    _serial.Read(_buffer, 0, nReadBytes);
                    // parse message
                    int i = 0;
                    x0 = 0;
                    y0 = 0;
                    x1 = 0;
                    y1 = 0;
                    while (i < nReadBytes - 5)
                    {
                        if (_buffer[i] == 0)
                        {
                            // check packet loss here by checking counter
                            if (packetNum == 0 || ((packetNum % 255 + 1) == (int)_buffer[i + 1]))
                                packetNum = (int)_buffer[i + 1];
                            else
                                missingPacket++;

                            x0 += (int)_buffer[i + 2] - 128;
                            y0 += (int)_buffer[i + 3] - 128;
                            x1 += (int)_buffer[i + 4] - 128;
                            y1 += (int)_buffer[i + 5] - 128;
                            i += PACKET_SIZE;
                            readCount++;
                        }
                        else
                        {
                            // check if packet is corrupted
                            while (i < nReadBytes && _buffer[i] != 0) i++;
                            errorCount++;
                        }
                    }
                    x0_cum += x0;
                    y0_cum += y0;
                    x1_cum += x1;
                    y1_cum += y1;

                    dz += y0 + y1;
                    dx += y0 - y1;
                    // parse message end
                }
            }
            Debug.Log("Thread finished");
        }
    }
}
