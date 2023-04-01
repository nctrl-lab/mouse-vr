using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

// Showing the basic usage of TcpListener
public class TcpListenerTest : MonoBehaviour
{
    TcpListener listener;
    Thread SocketThread;
    volatile bool keepReading = false;

    void Start()
    {
        SocketThread = new Thread(ThreadFunction);
        SocketThread.IsBackground = true;
        SocketThread.Start();
    }

    void OnDisable()
    {
        keepReading = false;

        //stop thread
        if (SocketThread != null)
        {
            SocketThread.Abort();
        }

        if (listener != null)
        {
            listener.Stop();
        }
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

    void ThreadFunction()
    {
        // Data buffer for incoming data.
        byte[] bytes = new Byte[1024];
        string data;

        // host running the application.
        Debug.Log("IP " + getIPAddress().ToString());
        IPAddress[] ipArray = Dns.GetHostAddresses(getIPAddress());
        IPEndPoint localEndPoint = new IPEndPoint(ipArray[0], 22224);

        try
        {
            // Create a TCP/IP socket.
            listener = new TcpListener(localEndPoint);
            listener.Start();

            // Start listening for connections.
            while (true)
            {
                keepReading = true;

                // Program is suspended while waiting for an incoming connection.
                Debug.Log("Waiting for Connection");     //It works
                using TcpClient client = listener.AcceptTcpClient();
                NetworkStream stream = client.GetStream();
                data = null;

                // An incoming connection needs to be processed.
                while (keepReading)
                {
                    bytes = new byte[1024];
                    int bytesRec = stream.Read(bytes, 0, bytes.Length);
                    data = Encoding.UTF8.GetString(bytes, 0, bytesRec);
                    Debug.Log("Received from Server: " + data);

                    // Stopping condition
                    if (bytesRec <= 0)
                    {
                        keepReading = false;
                        break;
                    }

                    Debug.Log("End of Receive");
                    Thread.Sleep(1);
                }

                Debug.Log("End of Accept");
                Thread.Sleep(1);
            }
        }
        catch (Exception e)
        {
            Debug.Log(e.ToString());
        }
        finally
        {
            listener.Stop();
        }
    }

}