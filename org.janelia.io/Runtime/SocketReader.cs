using System;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

// A lower-level reader.  Also supports some writing.
// `SocketMessageReader` is higher level, using the current class and adding notion of messages,
// with a designated delimiter (header or terminator).

namespace Janelia
{
    // TODO: Consider changing the name to `SocketClient`, since some writing is supported.
    public class SocketReader
    {
        // Supports UDP (the default) or TCP.
        public readonly bool usingUDP;
        public readonly bool usingTCPServer;
        public bool debug = true;

        // Setting this flag to `true` will reduce performance.,
        public bool debugSlowly = false;

        // Only TCP needs `connectRetryMs`.
        public SocketReader(string hostname = "127.0.0.1", int port = 22224, int bufferSizeBytes = 1024, int readBufferCount = 240, bool useTCPServer = true, bool useUDP = false, int connectRetryMs = 5000)
        {
            usingTCPServer = useTCPServer;
            usingUDP = useTCPServer ? false : useUDP;
            _hostname = useTCPServer ? getIPAddress() : hostname;
            _port = port;
            _connectRetryMs = connectRetryMs;
            _bufferSizeBytes = bufferSizeBytes;
            _readBufferCount = readBufferCount;
            _ringBuffer = new RingBuffer(_readBufferCount, _bufferSizeBytes);
            _writeBuffer = new Byte[_bufferSizeBytes];
        }

        private string getIPAddress()
        {
            IPHostEntry host;
            string localIP = "";
            host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    localIP = ip.ToString();
                }

            }
            return localIP;
        }

        ~SocketReader()
        {
            OnDisable();
        }

        public void Start()
        {
            if (debug)
                Debug.Log(Now() + "SocketReader.Start() creating socket thread");


            _thread = usingUDP ?
                new System.Threading.Thread(ThreadFunctionUDP) { IsBackground = true } :
                new System.Threading.Thread(ThreadFunctionTCP) { IsBackground = true };
            _thread.Start();
        }

        // Take some bytes from the buffer of what was read from the socket.
        public bool Take(ref Byte[] taken, ref long timestampMs)
        {
            return _ringBuffer.Take(ref taken, ref timestampMs);
        }

        public bool ReadyToWrite()
        {
            if (usingUDP)
            {
                return false;
            }

            // A cheap-and-cheerful way to wait until `WriteThreadFunctionTCP` is ready
            // for the `Monitor.Pulse(_writeLock)`.
            for (int i = 0; i < 10; ++i)
            {
                lock (_writeThreadInitializedLock)
                {
                    if (_writeThreadInitialized)
                    {
                        return true;
                    }
                }
                System.Threading.Thread.Sleep(100);
            }

            if (debug)
                Debug.Log(Now() + "SocketReader.Write() failed: thread not initialized");

            return false;
        }

        // Write some data to the socket.
        public void Write(Byte[] toWrite)
        {
            Write(toWrite, toWrite.Length);
        }

        public void Write(Byte[] toWrite, int sizeToWrite)
        {
            if (!usingUDP)
            {
                lock (_writeLock)
                {
                    // TODO: Consider supporting multiple writes with a ring buffer.
                    Buffer.BlockCopy(toWrite, 0, _writeBuffer, 0, sizeToWrite);
                    _writeOffset = 0;
                    _writeLength = sizeToWrite;

                    if (debug)
                        Debug.Log(Now() + "SocketReader.Write() Monitor.Pulse");

                    System.Threading.Monitor.Pulse(_writeLock);
                }
            }
        }

        public void OnDisable()
        {
            if (_thread != null)
            {
                if (debug)
                    Debug.Log(Now() + "SocketReader.OnDisable() aborting socket thread");

                _thread.Abort();
                _thread = null;

                if (_clientSocket != null)
                {
                    _clientSocket.Close();
                    _clientSocket = null;
                }

                if (_serverSocket != null)
                {
                    _serverSocket.Stop();
                    _serverSocket = null;
                }
            }

            if (_writeThread != null)
            {
                if (debug)
                    Debug.Log(Now() + "SocketReader.OnDisable() aborting socket thread [write]");

                _writeThread.Abort();
                _writeThread = null;
            }
        }

        private void ThreadFunctionUDP()
        {
            try
            {
                if (debug)
                    Debug.Log(Now() + "SocketReader using UDP");

                // The `UdpClient` class does not seem to support a version of `Receive` that will reuse a
                // `byte []` buffer passed in as argument.  So, to avoid possible problems with garbage collection,
                // use the lower-level `Socket` approach.
                Socket socket;

                try
                {
                    if (debug)
                        Debug.Log(Now() + "SocketReader setting up socket for host '" + _hostname + "' port " + _port);

                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);

                    socket.Bind(new IPEndPoint(IPAddress.Parse(_hostname), _port));
                }
                catch (SocketException socketException)
                {
                    if (debug)
                        Debug.Log(Now() + "SocketReader cannot set up socket: " + socketException);
                    return;
                }

                Byte[] readBuffer = new Byte[_bufferSizeBytes];
                while (true)
                {
                    try
                    {
                        if (debugSlowly)
                            Debug.Log(Now() + "SocketReader waiting to receive UDP datagram");

                        int length = socket.Receive(readBuffer);

                        if (debugSlowly)
                            Debug.Log("SocketReader read " + length + " bytes");

                        _ringBuffer.Give(readBuffer);
                        Array.Clear(readBuffer, 0, length);

                        if (debugSlowly)
                            Debug.Log("SocketReader added " + length + " bytes to the ring buffer");
                    }
                    catch (SocketException socketException)
                    {
                        if (debug)
                            Debug.Log(Now() + "SocketReader exception when during receive: " + socketException);
                    }
                }
            }
            catch (System.Threading.ThreadAbortException) {}
        }

        private void ThreadFunctionTCP()
        {
            if (debug)
                Debug.Log(Now() + "SocketReader using TCP");

            Byte[] readBuffer = new Byte[_bufferSizeBytes];

            // Starting TCP server
            if (usingTCPServer)
            {
                IPAddress[] ipArray = Dns.GetHostAddresses(_hostname);
                _serverSocket = new TcpListener(ipArray[0], _port);
                _serverSocket.Start();
            }

            while (true)
            {
                try
                {
                    if (usingTCPServer)
                    {
                        if (debug)
                            Debug.Log(Now() + "SocketReader is waiting for the client at IP '" + _hostname + "' port " + _port);
                        _clientSocket = _serverSocket.AcceptTcpClient();
                    }
                    else
                    {
                        if (debug)
                            Debug.Log(Now() + "SocketReader trying to connect to server '" + _hostname + "' port " + _port);
                        _clientSocket = new TcpClient(_hostname, _port);
                    }

                    try
                    {
                        // Start the thread for writing here, so it can use the same `TcpClient` and `NetworkStream`.
                        _writeThread = new System.Threading.Thread(WriteThreadFunctionTCP) { IsBackground = true };
                        _writeThread.Start();

                        // Socket connected
                        Debug.Log(Now() + "SocketReader got stream connection at " + _port);
                        using (NetworkStream stream = _clientSocket.GetStream())
                        {
                            Debug.Log(Now() + "SocketReader got stream connection at " + _port);

                            int length;
                            while ((length = stream.Read(readBuffer, 0, readBuffer.Length)) != 0)
                            {
                                if (debugSlowly)
                                    Debug.Log("SocketReader read " + length + " bytes");

                                _ringBuffer.Give(readBuffer);
                                Array.Clear(readBuffer, 0, length);

                                if (debugSlowly)
                                    Debug.Log("SocketReader added " + length + " bytes to the ring buffer");
                            }
                        }
                        // Socket disconnected
                        Debug.Log("SocketReader lost connection");
                    }
                    catch (SocketException socketException) // Socket connection error
                    {
                        if (debug)
                            Debug.Log(Now() + "SocketReader reading socket exception: " + socketException);
                    }
                }
                catch (SocketException socketException)
                {
                    if (debug)
                    {
                        Debug.Log(Now() + "SocketReader connection socket exception: " + socketException);
                        Debug.Log(Now() + "SocketReader sleeping for " + _connectRetryMs + " ms before retrying");
                    }
                    System.Threading.Thread.Sleep(_connectRetryMs);
                }
                finally
                {
                    if (_writeThread != null)
                    {
                        _writeThread.Abort();
                        _writeThreadInitialized = false;
                    }
                }
            }
        }

        private void WriteThreadFunctionTCP()
        {
            try
            {
                // https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.networkstream.beginread?view=net-5.0#remarks
                // "Read and write operations can be performed simultaneously on an instance of the NetworkStream class
                // without the need for synchronization. As long as there is one unique thread for the write operations and
                // one unique thread for the read operations, there will be no cross-interference between read and write
                // threads and no synchronization is required."

                using (NetworkStream stream = _clientSocket.GetStream())
                {
                    if (debug)
                        Debug.Log(Now() + "SocketReader [write] got stream connection to server '" + _hostname + "' port " + _port);

                    lock (_writeThreadInitializedLock)
                    {
                        _writeThreadInitialized = true;
                    }

                    try {
                    while (true)
                    {
                        lock (_writeLock)
                        {
                            // Execution will continue here after the `Monitor.Pulse(_writeLock)`
                            // in the `Write` function on the main thread.
                            System.Threading.Monitor.Wait(_writeLock);

                            if (debug)
                                Debug.Log(Now() + "SocketReader about to write " + _writeLength + " bytes");

                            stream.Write(_writeBuffer, _writeOffset, _writeLength);
                        }
                    }
                    }
                    catch (SocketException socketException)
                    {
                        Debug.Log("Error");
                    }
                }
            }
            catch (SocketException socketException)
            {
                if (debug)
                    Debug.Log(Now() + "SocketReader [write] socket exception: " + socketException);
            }
        }

        private string Now()
        {
            if (Application.isEditor)
            {
                return "";
            }
            DateTime n = DateTime.Now;
            return "[" + n.Hour + ":" + n.Minute + ":" + n.Second + ":" + n.Millisecond + "] ";
        }

        private string _hostname;
        private int _port;
        private int _connectRetryMs;
        private int _bufferSizeBytes;
        private int _readBufferCount;

        private TcpListener _serverSocket;
        private TcpClient _clientSocket;

        private System.Threading.Thread _thread;
        private RingBuffer _ringBuffer;

        private System.Threading.Thread _writeThread;
        private readonly object _writeThreadInitializedLock = new object();
        private bool _writeThreadInitialized = false;
        private readonly object _writeLock = new object();
        private Byte[] _writeBuffer;
        private int _writeOffset;
        private int _writeLength;
    }
}
