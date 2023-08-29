using System;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using UnityEngine;

// Task-related logic comes here

namespace Janelia
{
    public class TaskController : MonoBehaviour
    {
        public bool debug = false;
        // API to control VR environment
        Vr vr = new Vr();
        
        // This is entered by MouseVR GUI (TaskManager.cs)
        // The content of note can be changed during task.
        public string animalName, task, note;

        // Task-related variables
        public int nTrial, iTrial, iCorrect, iReward, rewardAmountUl;
        public States iState;
        public Choices iTarget, iChoice;

        // Serial ports to Teensy (or BCS) to give reward (or optogenetics)
        public string comPort = "COM5";
        public SerialPort serial;

        // Socket communication
        public int socketPort = 22223;
        private SocketReader socket;
        private Byte[] socketBuffer = new Byte[1024];
        private string socketData = "";
        private long socketTimestampMs;
        Regex regchar = new Regex("[^_0-9a-zA-Z(),.']");
        Regex regex_s = new Regex(@"^(\w+)\.(\w+)\(\s*'*\s*(\w+)\s*'*\s*\)\n?");
        Regex regex_3 = new Regex(@"^(\w+)\.(\w+)\(\s*(-?\d+(\.\d+)?)\s*,\s*(-?\d+(\.\d+)?)\s*,\s*(-?\d+(\.\d+)?)\s*\)\n?");
        Regex regex_4 = new Regex(@"^(\w+)\.(\w+)\(\s*(-?\d+(\.\d+)?)\s*,\s*(-?\d+(\.\d+)?)\s*,\s*(-?\d+(\.\d+)?)\s*,\s*(-?\d+(\.\d+)?)\s*\)\n?");
        Regex regex_s3 = new Regex(@"^(\w+)\.(\w+)\(\s*'?\s*(\w+)\s*'?\s*,\s*(-?\d+(\.\d+)?)\s*,\s*(-?\d+(\.\d+)?),\s*(-?\d+(\.\d+)?)\s*\)\n?");
        Match match;

        // Task states
        public enum States
        {
            Choice = 0, // start of the trial, animal has to choose one side (left or right)
            Reward = 1 // conclusion of the trial, animal has chosen the side and got the reward (or not)
        }

        public enum Choices
        {
            None = 0,
            Left = 1,
            Right = 2
        }

        private void Start()
        {
            // Try to open serial port
            // If it fails just ignore it
            serial = new SerialPort(comPort, 115200);
            try
            {
                serial.Open();
                if (serial.IsOpen)
                {
                    _isOpen = true;
                }
            }
            catch
            {
                Debug.Log(comPort + " is not available");
            }

            // Try to open socket
            socket = new SocketReader("", socketPort);
            socket.Start();

            LogParameter(); // Log task parameters (animal name, task, trial number, reward amount per trial)
            Reset(); // Reset trial-related variables

            vr.Start(); // This gets the list of object that needs to be controlled during task.
        }


        // Task logic start here,
        // when the player (animal) hits the objects with a specific naming (_objectname_r_)
        private void OnTriggerEnter(Collider other)
        {
            string[] subnames = other.name.Trim('_').Split('_');
            if (subnames.Length==2 && subnames[1].Contains('r'))
            {
                note = other.name;

                AlternationTask(subnames[0]); // add or replace task here
            }
        }


        ///////// Task logic //////////
        private void AlternationTask(string e)
        {
            if (iState == States.Choice) // 'trial start'
            {
                if (e.StartsWith("l") || e.StartsWith("r")) // chosen the door
                {
                    iState = States.Reward; // move to 'trial end'
                    iChoice = (e.StartsWith("l")) ? Choices.Left : Choices.Right;
                    if (iTarget == Choices.None || iTarget == iChoice)
                    {
                        // Give reward if the chosen side and the target side match
                        iCorrect++;
                        iReward += rewardAmountUl;
                        Reward();
                    }
                    LogTrial(); // Log trial information at every state change
                }
            }
            else if (iState == States.Reward)
            {
                if (e.StartsWith("e")) // 'trial end'
                {
                    iState = States.Choice; // move to 'trial start'
                    iTarget = (iChoice==Choices.Left) ? Choices.Right : Choices.Left; // Alternation
                    LogTrial(); // Log trial information at every state change
                    PrintLog(); // Print on console terminal

                    vr.Teleport("10"); // To the starting position for the next trial
                    iTrial++;

                    // Finishing task condition
                    if (iTrial >= nTrial)
                        Quit();
                }
            }
        }

        private void Update()
        {
            // Reads messages from socket connection
            while (socket.Take(ref socketBuffer, ref socketTimestampMs))
            {
                socketData = System.Text.Encoding.UTF8.GetString(socketBuffer);
                // TODO: I tried to split by newline delimeters but failed...
                string[] msgs = socketData.Split('\n');
                foreach (string m in msgs)
                {
                    string msg = regchar.Replace(m, string.Empty);
                    if (msg.Length < 1) continue;
                    if (debug)
                    {
                        Debug.Log("Socket Message: " + msg);
                    }
                    JovianToVr(msg);
                }
            }
        }

        private void JovianToVr(string cmd)
        {
            try
            {
                cmd = cmd.ToLower();

                // toggle motion
                if (cmd.StartsWith("console.toggle_motion"))
                {
                    Vr.Connect();
                }

                // toggle display
                else if (cmd.StartsWith("console.toggle_blanking"))
                {
                    Vr.BlankDisplay();
                }

                // blank display
                else if (cmd.StartsWith("console.blank_display"))
                {
                    if (cmd.StartsWith("console.blank_display(1)"))
                        Vr.BlankDisplay(false);
                    else
                        Vr.BlankDisplay(true);
                }

                // teleport player
                else if (cmd.StartsWith("console.teleport"))
                {
                    match = regex_s.Match(cmd);
                    if (match.Success)
                    {
                        vr.Teleport(match.Groups[3].Value);
                    }

                    match = regex_3.Match(cmd);
                    if (match.Success)
                    {
                        float x = float.Parse(match.Groups[3].Value);
                        float z = float.Parse(match.Groups[5].Value);
                        float y = float.Parse(match.Groups[7].Value);
                        Vector3 position = new Vector3(x, y, z);
                        vr.Teleport(position);
                    }

                    match = regex_4.Match(cmd);
                    if (match.Success)
                    {
                        float x = float.Parse(match.Groups[3].Value);
                        float z = float.Parse(match.Groups[5].Value);
                        float y = float.Parse(match.Groups[7].Value);
                        Vector3 position = new Vector3(x, y, z);
                        float rotation = float.Parse(match.Groups[9].Value);
                        vr.Teleport(position, rotation);
                    }
                }

                // teleport object
                else if (cmd.StartsWith("model.move"))
                {
                    match = regex_s3.Match(cmd);
                    if (match.Success)
                    {
                        string name = match.Groups[3].Value;
                        float x = float.Parse(match.Groups[4].Value);
                        float z = float.Parse(match.Groups[6].Value);
                        float y = float.Parse(match.Groups[8].Value);
                        Vector3 position = new Vector3(x, y, z);
                        vr.Move(name, position);
                    }
                }

                else if (cmd.StartsWith("model.get_position"))
                {
                    match = regex_s.Match(cmd);
                    if (match.Success)
                    {
                        string name = match.Groups[3].Value;
                        Vector3 position = vr.GetPosition(name);
                        string msg = String.Format("{0:0F},{1:0F},{2:0F}",
                            1000 * position.x, 1000 * position.z, 1000 * position.y);
                        socket.Write(System.Text.Encoding.UTF8.GetBytes(msg));
                    }
                }

                // reward
                else if (cmd.StartsWith("reward"))
                {
                    Reward();
                }

                // quit
                else if (cmd.StartsWith("quit"))
                {
                    Quit();
                }

                else
                {
                    Debug.Log("JovianToVr Error: failed to parse " + cmd);
                }
            }
            catch (Exception e)
            {
                Debug.Log("JovianToVr Error: " + e);
            }
        }

        private void OnDisable()
        {
            if (_isOpen)
            {
                serial.Close();
            }
            socket.OnDisable();
        }

        private void Quit()
        {
            // This is basically the same as clicking the stop button
            UnityEditor.EditorApplication.isPlaying = false;
        }

        public void Reset()
        {
            iState = 0;
            iTrial = 0;
            iCorrect = 0;
            iTarget = Choices.None;
            iChoice = Choices.None;
            iReward = 0;
            note = "";
        }

        public void Reward()
        {
            if (_isOpen)
            {
                // Send message to Teensy to give the reward
                serial.Write("r");
            }
        }

        private void LogTrial()
        {
            taskLog.iState = iState;
            taskLog.iTrial = iTrial;
            taskLog.iCorrect = iCorrect;
            taskLog.iTarget = iTarget;
            taskLog.iChoice = iChoice;
            taskLog.iReward = iReward;
            taskLog.note = note;
            Logger.Log(taskLog);
        }

        private void PrintLog()
        {
            Debug.Log("trial: " + iTrial + ", correct: " + iCorrect);
        }

        private void LogParameter()
        {
            taskParametersLog.animalName = animalName;
            taskParametersLog.task = task;
            taskParametersLog.nTrial = nTrial;
            taskParametersLog.rewardAmountUl = rewardAmountUl;
            taskParametersLog.note = note;
            Logger.Log(taskParametersLog);
        }

        // Log for every trial
        [SerializeField]
        private class TaskLog : Logger.Entry
        {
            public States iState;
            public int iTrial;
            public int iCorrect;
            public Choices iTarget; // 1: left, 2: right
            public Choices iChoice; // 1: left, 2: right
            public int iReward; // total reward amount in uL 
            public string note;
        }; private TaskLog taskLog = new TaskLog();

        // Log for parameters
        [SerializeField]
        private class TaskParametersLog : Logger.Entry
        {
            public string animalName;
            public string task;
            public int nTrial;
            public int rewardAmountUl; // reward amount per trial
            public string note;
        }; private TaskParametersLog taskParametersLog = new TaskParametersLog();

        private bool _isOpen = false;
    }
}
