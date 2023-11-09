using System;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections;
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
        private float delayDuration;
        public float delayDurationStart = 10f;
        public float delayDurationMean = 30f;
        public float delayDurationEnd = 50f;
        public float punishmentLatency = 1.5f;
        public float punishmentDuration = 0f; // infinite if zero
        public float punishmentLength = 10f;

        // Serial ports to Teensy (or BCS) to give reward (or optogenetics)
        public string comPort = "COM4";
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
            Standby = 0,
            Start = 1, // start of the trial, used only once
            Delay = 2,
            Cue = 3,
            Success = 4, // automatically goes back to delay
            Failure = 5, // automatically goes back to delay
            FailureEnd = 6,
            Other = 7
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

        private void Update()
        {
            if (iState == States.Start)
            {
                if (task == "avoidance")
                    StartAvoidanceTask();
                else if (task == "gonogo")
                    StartGonogoTask();

            }

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

        // Task logic start here,
        // when the player (animal) hits the objects with a specific naming (_objectname_r_)
        private void OnTriggerEnter(Collider other)
        {
            string[] subnames = other.name.Trim('_').Split('_');
            if (subnames.Length==2 && subnames[1].Contains('r'))
            {
                note = other.name;

                if (task == "alternation")
                    AlternationTask(subnames[0]); // add or replace task here
                else if (task == "avoidance")
                    AvoidanceTask(subnames[0]);
            }
        }

        ///////// Task logic //////////
        private void AlternationTask(string e)
        {
            if (iState == States.Start) // 'trial start'
            {
                if (e.StartsWith("l") || e.StartsWith("r")) // chosen the door
                {
                    iChoice = (e.StartsWith("l")) ? Choices.Left : Choices.Right;
                    if (iTarget == Choices.None || iTarget == iChoice)
                    {
                        // Give reward if the chosen side and the target side match
                        iState = States.Success;
                        iCorrect++;
                        iReward += rewardAmountUl;
                        Reward();
                    }
                    else
                    {
                        iState = States.Failure;
                    }
                    LogTrial(); // Log trial information at every state change
                }
            }
            else
            {
                if (e.StartsWith("e")) // 'trial end'
                {
                    iState = States.Start; // move to 'trial start'
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


        private void AvoidanceTask(string e)
        {
            // Delay
            // Cue
            // --> Success --> Outcome
            // --> Fail --> Air puff / Failure
            // --> End of hallway (or automatic restart?) --> Outcome

            if (e.StartsWith("target"))
            {
                if (iState == States.Cue)
                {
                    iState = States.Success;
                    iCorrect++;
                }
                else if (iState == States.Failure || iState == States.FailureEnd)
                {
                    iState = States.Other;
                }
                CueOff();
                PunishmentOff();
                LogTrial();
                PrintLog();
                StartAvoidanceTask();
            }
            else if (e.StartsWith("end"))
            {
                // Stop current trial and restart
                CancelInvoke();
                CueOff();
                PunishmentOff();
                iState = States.Start;
                vr.Teleport("0");
            }
        }

        // Avoidance task related functions
        private void StartAvoidanceTask()
        {
            if (iState == States.Start || iState == States.Success || iState == States.FailureEnd || iState == States.Other) {
                iState = States.Delay;
                iTrial++;
                note = "delay";
                LogTrial();
                Invoke("CueOn", GetDelay());
            }
        }

        // Go/No-Go task logic
        // Trial start:
        //  - Teleport to starting point
        //  - Place cue at certain distance (TODO: should it be hidden until very close???)
        // Trial cue:
        //  - Enter target area
        //  - Start timer to check velocity
        // Trial success:
        //  - Reward is givin
        // Trial fail:
        //  - Timer is stopped
        //  - No reward given
        // * Cue: has start/fail/teloport bar
        private void StartGonogoTask()
        {
            if (iState == States.Start || iState == States.Other) {
                    iState = States.Cue;
                    CueOff();
                    PunishmentOff();
                    iState = States.Start;
                    vr.Teleport("0");
                    iTrial++;

                    // Finishing task condition
                    if (iTrial > nTrial)
                        Quit();
                }
            }
        }

        private float GetDelay()
        {
            var rand = new System.Random();
            double r = rand.NextDouble();
            if (r == 0) r = Single.MinValue;
            delayDuration = (float) Math.Min(delayDurationStart - (delayDurationMean-delayDurationStart) * Math.Log(r), delayDurationEnd);
            Debug.Log("Delay duration: " + delayDuration + " s");
            return delayDuration;
        }

        private void CueOn()
        {
            if (task == 'avoidance') {
                if (iState == States.Delay) {
                    iState = States.Cue;
                    note = "cue";
                    Invoke("CheckSuccess", punishmentLatency);
                    if (punishmentDuration > 0)
                        Invoke("CheckFailure", punishmentDuration);
                    Vector3 curpos = vr.GetPosition();
                    vr.Move("cue", new Vector3(0f, 0f, curpos.z-4.5f+punishmentLength/10));
                    LogTrial();
                }
            }
            else if (task == 'gonogo') {

            }
        }

        private void CueOff()
        {
            if (task == 'avoidance')
                vr.Move("cue_a", new Vector3(0f, -2f, 0f));
            else if (task == 'gonogo')
                vr.Move("cue_g", new Vector3(0f, -2f, 0f));
        }

        public void Reset()
        {
            iState = States.Standby;
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

        public void PunishmentOn()
        {
            if (_isOpen)
            {
                serial.Write("p");
            }
        }

        public void PunishmentOff()
        {
            if (_isOpen)
            {
                serial.Write("0");
            }
        }
        public void CheckSuccess()
        {
            if (iState == States.Cue)
            {
                iState = States.Failure;
                PunishmentOn();
                LogTrial();
            }
        }

        public void CheckFailure()
        {
            if (iState == States.Failure)
            {
                iState = States.FailureEnd;
                CueOff();
                PunishmentOff();
                LogTrial();
                PrintLog();
                StartAvoidanceTask();
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
            PunishmentOff();
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


        private void LogTrial()
        {
            taskLog.iState = iState;
            taskLog.iTrial = iTrial;
            taskLog.iCorrect = iCorrect;
            taskLog.iTarget = iTarget;
            taskLog.iChoice = iChoice;
            taskLog.iReward = iReward;
            taskLog.delayDuration = delayDuration;
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
            taskParametersLog.delayDurationStart = delayDurationStart;
            taskParametersLog.delayDurationMean = delayDurationMean;
            taskParametersLog.delayDurationEnd = delayDurationEnd;
            taskParametersLog.punishmentLatency = punishmentLatency;
            taskParametersLog.punishmentDuration = punishmentDuration;
            taskParametersLog.punishmentLength = punishmentLength;
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
            public float delayDuration;
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
            public float delayDurationStart;
            public float delayDurationMean;
            public float delayDurationEnd;
            public float punishmentLatency;
            public float punishmentDuration; // infinite if zero
            public float punishmentLength;
            public string note;
        }; private TaskParametersLog taskParametersLog = new TaskParametersLog();

        private bool _isOpen = false;
    }
}
