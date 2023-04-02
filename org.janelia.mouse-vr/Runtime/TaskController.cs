using System;
using System.IO.Ports;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

// Task-related logic comes here

namespace Janelia
{
    public class TaskController : MonoBehaviour
    {
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
        public SocketReader socket;
        private Byte[] socketBuffer = new Byte[1024];
        private string socketData = "";
        private long socketTimestampMs;

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

        // Reads messages from socket connection
        private void Update()
        {

            while (socket.Take(ref socketBuffer, ref socketTimestampMs))
            {
                socketData += System.Text.Encoding.UTF8.GetString(socketBuffer);
            }

            while (socketData.Length > 0 && socketData.IndexOf('\n') >= 0)
            {
                string[] cmds = socketData.Split('\n', 2);
                JovianToVr(socketData);
                socketData = cmds[1];
            }
        }

        private void JovianToVr(string cmd)
        {
            cmd = cmd.ToLower();

            // toggle motion
            if (cmd.StartsWith("console.toggle_motion"))
            {
                vr.Connect();
            }

            // toggle motion
            else if (cmd.StartsWith("console.toggle_blanking"))
            {
                vr.BlankDisplay();
            }

            // blank display
            else if (cmd.StartsWith("console.blank_display"))
            {
                if (cmd.StartsWidth("console.blank_display(1)"))
                    vr.BlankDisplay(false);
                else
                    vr.BlankDisplay(true);
            }

            // teleport player
            else if (cmd.StartsWith("console.teleport"))
            {
                string[] opt = cmd.Replace("console.teleport(", "").TrimEnd(')').Split(',');
                Vector3 position = new Vector3(float.Parse(opt[0]), float.Parse(opt[2]), float.Parse(opt[1]));
                float rotation = float.Parse(opt[3]); // east 0 north 90 west +-/180 south -90
                vr.Teleport(position, rotation);
            }

            // teleport object
            else if (cmd.StartsWith("model.move"))
            {
                string[] opt = cmd.Replace("model.move(", "").TrimEnd(')').Split(',');
                string name = opt[0].Trim('\'');
                Vector3 position = new Vector3(float.Parse(opt[1]), float.Parse(opt[3]), float.Parse(opt[2]));
                vr.Move(name, position);
            }

            else if (cmd.StartsWith("model.get_position"))
            {
                string[] opt = cmd.Replace("model.get_position(", "").TrimEnd(')');
                string name = opt[0].Trim('\'');
                Vector3 position = vr.GetPosition(name);
                string msg = String.Format("{0:0F},{1:0F},{2:0F}",
                    1000 * position.x, 1000 * position.z, 1000 * position.y);
                socket.Write(System.Text.Encoding.UTF8.GetBytes(msg));
            }

            // reward
            else if (cmd.StartsWith("reward"))
            {
                Reward()
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
