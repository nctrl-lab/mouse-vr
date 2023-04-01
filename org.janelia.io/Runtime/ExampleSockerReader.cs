using System;
using UnityEngine;

namespace Janelia
{
    public class ExampleSockerReader : MonoBehaviour
    {
        SocketReader reader = new SocketReader();
        Byte[] buffer = new Byte[1024];
        long timestamp;
        string data;
        bool canWrite = false;

        // Start is called before the first frame update
        void Start()
        {
            reader.debug = true;
            reader.Start();
            InvokeRepeating("SendMessage", 5f, 1f);
        }

        // Update is called once per frame
        void Update()
        {
            // Test reading
            if (reader.Take(ref buffer, ref timestamp))
            {
                data = System.Text.Encoding.UTF8.GetString(buffer);
                Debug.Log(data);
            }

        }

        void SendMessage()
        {
            if (!canWrite && reader.ReadyToWrite())
                canWrite = true;
            if (canWrite)
                reader.Write(System.Text.Encoding.UTF8.GetBytes(String.Format("{0:N},2,3,4,5,6,7,8,9,0", Time.frameCount)));
        }


        void OnDisable()
        {
            reader.OnDisable();
        }
    }
}