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
        public int nTrial, iTrial, iCorrect, iReward, rewardAmount = 10;
        private int rewardAmountPrev = 0;
        public States iState;
        public Cues pCue, iCue;
        private int iCueRepeat, maxCueRepeat = 3;
        public Choices iChoice;
        private float delayDuration;
        public float delayDurationStart = 30f;
        public float delayDurationMean = 60f;
        public float delayDurationEnd = 120f;
        public float rewardLatency = 0.0f;
        public float punishmentLatency = 1.5f;
        public float punishmentDuration = 10f; // infinite if zero

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
            Choice = 3,
            Success = 4, // automatically goes back to delay
            Failure = 5, // automatically goes back to delay
            Other = 6
        }

        public enum Cues
        {
            None = 0,
            Nogo = 1,
            Go = 2
        }

        public enum Choices
        {
            None = 0,
            Left = 1,
            Right = 2
        }

        private void Start()
        {

            // Serial: air puff
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
                Debug.Log(serial + " is not available");
            }

            // Try to open socket for external communication
            socket = new SocketReader("", socketPort);
            socket.Start();

            LogParameter(); // Log task parameters (animal name, task, trial number, reward amount per trial)
            Reset(); // Reset trial-related variables

            SetRewardAmount();
            SetPunishmentDuration();

            vr.Start(); // This gets the list of object that needs to be controlled during task.

        }

        private void Update()
        {
            if (iState == States.Start)
            {
                try {
                    note = "start";
                    Invoke(task, 0f);
                }
                catch (Exception e)
                {
                    Debug.Log("Update Error: " + e);
                }
            }

            if (Input.GetKeyDown("r"))
            {
                Reward();
                iReward += rewardAmount;
                Debug.Log("Reward");
            }
            else if (Input.GetKeyDown("p"))
            {
                PunishmentOn();
                Debug.Log("Punishment on");
            }
            else if (Input.GetKeyDown("f"))
            {
                FlushWater();
            }
            else if (Input.GetKeyDown("0"))
            {
                PunishmentOff();
                Debug.Log("Punishment off");
            }
            else if (Input.GetKeyDown("h"))
            {
                Debug.Log("==== Helpful keys ====");
                Debug.Log("r: give the water reward");
                Debug.Log("p: start the air puff");
                Debug.Log("0: stop the air puff");
                Debug.Log("f: flush water for 1 second");
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

        // When the player (animal) hits the objects with a specific naming (_objectname_r_)
        private void OnTriggerEnter(Collider other)
        {
            note = other.name.Trim('_');
            if (note.EndsWith('r'))
            {
                try {
                    Invoke(task, 0f);
                }
                catch (Exception e)
                {
                    Debug.Log("OnTriggerEnter Error: " + e);
                }
            }
        }

        ///////// Task logic //////////
        public void Reset()
        {
            iState = States.Standby;
            iTrial = 0;
            iCorrect = 0;
            iCue = Cues.None;
            pCue = Cues.None;
            iChoice = Choices.None;
            iReward = 0;
            note = "";
        }

        private void Nogo()
        {
            // 1. Start: teleport animal / place reward cue
            // 2. Delay: wait until to touch target
            if (note == "start")
            {
                iState = States.Delay;
                iCue = Cues.Nogo;
                iTrial++;
                CueOn();
                CheckRewardAmount();
                LogTrial();
            }
            else if (note.StartsWith("choice"))
            {
                if (iState == States.Delay)
                {
                    iState = States.Choice;
                    LogTrial();
                    Invoke("CheckResult", rewardLatency);
                }
            }
            else if (note.StartsWith("target"))
            {
                if (iState == States.Choice)
                {
                    iState = States.Failure;
                    LogTrial();
                    Debug.Log(iState);
                }
                // No response if Success already happened.
            }
            else if (note.StartsWith("end"))
            {
                // Stop current trial and restart
                CancelInvoke();
                if (iTrial < nTrial)
                {
                    iState = States.Start;
                    PrintLog();
                }
                else
                {
                    iState = States.Standby;
                    LogTrial();
                    PunishmentOff();
                    Quit();
                }
            }
        }

        private void Nogogo()
        {
            // 1. Start: teleport animal / place reward cue
            // 2. Delay: wait until to touch target
            if (note == "start")
            {
                iState = States.Delay;
                iTrial++;
                NextCue(2);
                CueOn();
                CheckRewardAmount();
                LogTrial();
            }
            else if (note.StartsWith("choice"))
            {
                if (iState == States.Delay)
                {
                    iState = States.Choice;
                    LogTrial();
                    if (iCue == Cues.Nogo)
                        Invoke("CheckResult", rewardLatency);
                    else
                        Invoke("CheckResult", punishmentLatency);
                }
            }
            else if (note.StartsWith("target"))
            {
                if (iState == States.Choice)
                {
                    if (iCue == Cues.Nogo) {
                        iState = States.Failure;
                    }
                    else if (iCue == Cues.Go) {
                        iState = States.Success;
                    }
                }
                else {
                    iState = States.Other;
                }
                LogTrial();
                PunishmentOff();
                // No response if outcome already happened.
            }
            else if (note.StartsWith("end"))
            {
                // Stop current trial and restart
                CancelInvoke();
                CueOff();
                if (iTrial < nTrial)
                {
                    iState = States.Start;
                    PrintLog();
                }
                else
                {
                    iState = States.Standby;
                    LogTrial();
                    Quit();
                }
            }
        }

        public void NextCue(int nCue)
        {
            var rnd = new System.Random();
            if (iCue == Cues.None) {
                iCue = (Cues)rnd.Next(1, nCue+1);
            }
            else {
                pCue = iCue;
                iCue = (Cues)rnd.Next(1, nCue+1);
                if (pCue == iCue) {
                    iCueRepeat++;
                    if (iCueRepeat > maxCueRepeat) {
                        int rndidx = rnd.Next(1, nCue);
                        if (rndidx >= (int)pCue)
                            rndidx++;
                        iCue = (Cues)rndidx;
                        iCueRepeat = 0;
                    }
                }
            }
        }

        public void CheckResult()
        {
            if (task.StartsWith("Nogo"))
            {
                if (iState == States.Choice)
                {
                    if (iCue == Cues.Nogo)
                    {
                        iState = States.Success;
                        iCorrect++;
                        note = "";
                        Reward();
                        LogTrial();
                        Debug.Log(iState);
                    }
                    else if (iCue == Cues.Go)
                    {
                        iState = States.Failure;
                        note = "";
                        PunishmentOn();
                        LogTrial();
                        Debug.Log(iState);
                    }
                }
                // Nothing happens if Success or Failure happend already.
            }
        }

        private float GetDelay()
        {
            var rand = new System.Random();
            double r = rand.NextDouble();
            if (r == 0) r = Single.MinValue;
            delayDuration = (float) Math.Min(delayDurationStart - (delayDurationMean-delayDurationStart) * Math.Log(r), delayDurationEnd);
            return delayDuration;
        }

        private float GetDistance()
        {
            // uses the same parameter as GetDelay, but quantized the distance to make smooth teleport.
            float distance = (float) Math.Round(GetDelay() / 15.0f) * 1.50f;
            Debug.Log("Delay distance: " + 10f * distance + " cm");
            return distance;
        }

        private void CueOn()
        {
            if (task.StartsWith("Nogo"))
            {
                vr.Teleport("0");
                if (iCue == Cues.Nogo)
                    vr.Move("cue_ng", new Vector3(0f, 0f, GetDistance()));
                else
                    vr.Move("cue_g", new Vector3(0f, 0f, GetDistance()));
            }
        }

        private void CueOff()
        {
            if (task.StartsWith("Nogo"))
            {
                vr.Move("cue_ng", new Vector3(0f, -2f, 0f));
                vr.Move("cue_g", new Vector3(0f, -2f, 0f));
            }
        }

        public void Reward()
        {
            if (_isOpen)
            {
                // Send message to Teensy to give the reward
                serial.Write("r");
                iReward += rewardAmount;
            }
        }

        public void flushWater() {
            if (_isOpen) {
                serial.Write("f\n");
                Debug.Log("Flush water for 1 second");
            }
        }

        public void SetRewardAmount()
        {
            if (_isOpen) {
                serial.Write("v" + rewardAmount + "\n");
                Debug.Log("Reward amount: " + rewardAmount + " ul");
            }
        }

        public void SetPunishmentDuration()
        {
            if (_isOpen) {
                serial.Write("f" + (int)punishmentDuration+ "\n");
                Debug.Log("Punishment duration: " + (int)punishmentDuration + " ul");
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
            taskLog.iCue = iCue;
            taskLog.iChoice = iChoice;
            taskLog.iReward = iReward;
            taskLog.delayDuration = delayDuration;
            taskLog.note = note;
            Logger.Log(taskLog);
        }

        private void PrintLog()
        {
            Debug.Log("trial: " + iTrial + ", correct: " + iCorrect + ", reward: " + iReward + " ul");
        }

        private void LogParameter()
        {
            taskParametersLog.animalName = animalName;
            taskParametersLog.task = task;
            taskParametersLog.nTrial = nTrial;
            taskParametersLog.rewardAmount = rewardAmount;
            taskParametersLog.rewardLatency = rewardLatency;
            taskParametersLog.delayDurationStart = delayDurationStart;
            taskParametersLog.delayDurationMean = delayDurationMean;
            taskParametersLog.delayDurationEnd = delayDurationEnd;
            taskParametersLog.punishmentLatency = punishmentLatency;
            taskParametersLog.punishmentDuration = punishmentDuration;
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
            public Cues iCue; // 1: left, 2: right
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
            public int rewardAmount; // reward amount per trial
            public float rewardLatency;
            public float delayDurationStart;
            public float delayDurationMean;
            public float delayDurationEnd;
            public float punishmentLatency;
            public float punishmentDuration; // infinite if zero
            public string note;
        }; private TaskParametersLog taskParametersLog = new TaskParametersLog();

        private bool _isOpen = false;
    }
}
