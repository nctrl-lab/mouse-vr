using System;
using UnityEngine;

namespace Janelia
{
    public class MouseTreadmillParser
    {
        public const int BYTE_PER_PACKET = 12;
        public const int CLIP_PIXEL = 12; // During testing, the maximal pixel value was around 10.

        public struct Message
        {
            public UInt64 readTimestampMs;
            public int x0;
            public int y0;
            public int x1;
            public int y1;
        }

        public static bool ParseMessage(ref Message message, Byte[] readData, long readTimestampMs)
        {
            int x0 = 0;
            int y0 = 0;
            int x1 = 0;
            int y1 = 0;
            
            // Accumulate 10 packets here
            for (int i = 0; i < readData.Length; i+=BYTE_PER_PACKET)
            {
                if (readData[i] != 0)
                {
                    Debug.Log("MouseTreadmillParser.ParseMessage: packet error");
                    return false;
                }
                
                int t0 = (int)readData[i+3] - 128;
                if (t0 > CLIP_PIXEL || t0 < -CLIP_PIXEL)
                {
                    Debug.Log("MouseTreadmillParser.ParseMessage: too high y0");
                    t0 = Mathf.Clamp(t0, -CLIP_PIXEL, CLIP_PIXEL);
                    t0 = (t0 > CLIP_PIXEL) ? CLIP_PIXEL : (t0 < -CLIP_PIXEL) ? -CLIP_PIXEL : t0;
                }

                int t1 = (int)readData[i+5] - 128;
                if (t1 > CLIP_PIXEL || t1 < -CLIP_PIXEL)
                {
                    Debug.Log("MouseTreadmillParser.ParseMessage: too high y1");
                    t1 = Mathf.Clamp(t1, -CLIP_PIXEL, CLIP_PIXEL);
                }

                x0 += (int)readData[i+2] - 128;
                x1 += (int)readData[i+4] - 128;
                y0 += t0;
                y1 += t1;
            }

            message.readTimestampMs = (UInt64)readTimestampMs;
            message.x0 = x0;
            message.y0 = y0;
            message.x1 = x1;
            message.y1 = y1;

            return true;
        }
    }
}