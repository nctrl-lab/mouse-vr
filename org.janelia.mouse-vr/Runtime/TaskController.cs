using System;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Collections;
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
        public int nTrial, iTrial, iTrial1, iTrial2;
        public int iCorrect, iCorrect1, iCorrect2;

        public States iState;
        public Choices iChoice, iCue;
        public int pType = -1;
        public bool useCueCounter = true;                // true: anti-bias cue (nextCueCounter); false: full random 50/50
        public double choiceEmaAlpha = 0.3;              // EMA weight for the right-choice estimate (0..1)
        public double[] avgRight = {0.5, 0.5, 0.5, 0.5}; // right-choice rate per pType
        public int iSuccess = 0;

        public int iReward, rewardAmount = 10;   // uL per reward (from calibration)
        public int rewardDuration = 60;          // valve-open time in ms (from calibration)
        public int rewardMax = 1500;

        // Beacon ITI
        public float ITI = 2.0f;
        public float successITI = 2.0f;
        public float failureITI = 10.0f;

        // Serial ports to Teensy (or BCS) to give reward (or optogenetics)
        public string comPort = "COM3";
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

        // One shared RNG; a new System.Random() per call can repeat draws within a clock tick.
        private System.Random rnd = new System.Random();

        // Task states
        public enum States
        {
            Standby = 0,
            Start = 1,   // set at the start of every trial (by Blackout/Restart); Update runs the task
            Delay = 2,
            Choice = 3,
            Success = 4, // outcome; Blackout then sets Start for the next trial
            Failure = 5, // outcome; Blackout then sets Start for the next trial
            Other = 6    // out-of-state trigger; ignored
        }

        // Left=1, Right=2 are used as numbers in nextCueCounter(); don't reorder.
        public enum Choices
        {
            None = 0,
            Left = 1,
            Right = 2
        }

        private void Start()
        {
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

            LogParameter();
            Reset();

            SetRewardDuration();

            vr.Start(); // collect the controllable scene objects
        }

        private void Update()
        {
            if (iState == States.Start)
            {
                try {
                    note = "start";
                    Vr.BlankDisplay(false); // a trial is beginning: show the scene
                    if (_isOpen && !_sessionStarted)
                    {
                        serial.Write("s"); // session start, once per session
                        _sessionStarted = true;
                    }
                    Invoke(task, 0f);
                }
                catch (Exception e)
                {
                    Debug.Log("Update Error: " + e);
                }
            }

            // Reads messages from socket connection
            while (socket != null && socket.Take(ref socketBuffer, ref socketTimestampMs))
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
            iTrial1 = 0;
            iTrial2 = 0;
            iCorrect1 = 0;
            iCorrect2 = 0;
            iChoice = Choices.None;
            iCue = Choices.None;
            iReward = 0;
            note = "";
            _sessionStarted = false; // re-arm the session-start 's'

            // Beacon countermeasure state: start unbiased.
            pType = -1;
            iSuccess = 0;
            for (int i = 0; i < avgRight.Length; i++)
                avgRight[i] = 0.5;
        }

        // Keep running until the trial or reward budget runs out.
        private bool SessionRunning => iTrial < nTrial && iReward < rewardMax;

        //////////////////////////////////////////////////////////////////////////////////////////////////////////
        public void Linear()
        {
            /// linear task ///
            // 1. Start: teleport animal to start position
            if (note == "start")
            {
                iState = States.Delay;
                vr.Teleport("linear");
                iTrial++;
                LogTrial();
            }
            // 2. End: reached the end trigger -> reward and finish the trial
            else if (note.StartsWith("end")) // trial end & reward
            {
                CancelInvoke(); // drop any queued Invoke before the ITI
                if (iState == States.Delay)
                {
                    iState = States.Success;
                    Reward();
                    LogTrial();
                }
                else {
                    iState = States.Other;
                }
                if (SessionRunning)
                {
                    StartCoroutine(Blackout(successITI)); // black screen for the success ITI
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

        // p == "" -> Beacon (cue only); p == "e" -> EasyBeacon (door + blocker block the
        // wrong side, so only the cued side is reachable). The wrappers below stay
        // parameterless so Invoke(task) can call them by name.
        private void BeaconImpl(string p)
        {
            /// Beacon task ///
            if (note == "start")
            {
                if (_isOpen)
                {
                    serial.Write("S"); // trial start (delay start)
                }
                nextCueCounter();
                iState = States.Delay;
                iTrial++;
                vr.Teleport("beacon");
                LogTrial();
            }
            else if (note.StartsWith("cue")) // delay end: reveal the beacon on the cued side
            {
                iState = States.Choice;
                if (_isOpen)
                {
                    serial.Write(iCue == Choices.Left ? "L" : "R"); // cued side
                }
                CueOn(p);
                LogTrial();
            }
            else if (note.StartsWith("left") || note.StartsWith("right")) // animal reached a side
            {
                if (iState != States.Choice)
                {
                    iState = States.Other;
                    return;
                }

                // chosen side; correct if it matches the cue.
                iChoice = note.StartsWith("left") ? Choices.Left : Choices.Right;
                bool correct = iChoice == iCue;
                if (correct)
                {
                    iState = States.Success;
                    iSuccess = 1;
                    iCorrect++;
                    ITI = successITI;
                    Reward();
                }
                else
                {
                    iState = States.Failure;
                    iSuccess = 0;
                    ITI = failureITI;
                }

                RecordChoice(iChoice, correct);
                EndBeaconTrial(p);
            }
        }

        public void Beacon() { BeaconImpl(""); }
        public void EasyBeacon() { BeaconImpl("e"); }

        // Send the chosen side ('l'/'r') and tally the per-side counts.
        private void RecordChoice(Choices choice, bool correct)
        {
            if (choice == Choices.Left)
            {
                if (_isOpen) serial.Write("l");
                iTrial1++;
                if (correct) iCorrect1++;
            }
            else
            {
                if (_isOpen) serial.Write("r");
                iTrial2++;
                if (correct) iCorrect2++;
            }
        }

        // End of a Beacon trial: clear the cue, log, then ITI blackout or quit.
        private void EndBeaconTrial(string p = "")
        {
            CueOff(p);
            CancelInvoke();
            LogTrial();
            if (SessionRunning)
            {
                StartCoroutine(Blackout(ITI));
                PrintLog();
            }
            else
            {
                iState = States.Standby;
                PrintLog();
                LogTrial();
                Quit();
            }
        }
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        public IEnumerator Blackout(float delay)
        {
            iState = States.Delay;
            Vr.BlankDisplay(true);
            yield return new WaitForSeconds(delay);
            iState = States.Start; // next trial
        }

        public void Restart()
        {
            CancelInvoke();
            iState = States.Failure;
            LogTrial();
            if (SessionRunning)
            {
                iState = States.Start;
                PrintLog();
            }
            else
            {
                iState = States.Standby;
                PrintLog();
                Quit();
            }
        }

        public void nextCueCounter()
        {
            // Full-random mode: unbiased 50/50 cue every trial, ignoring choice history.
            if (!useCueCounter)
            {
                iCue = (Choices)rnd.Next(1, 3);
                return;
            }

            // Cue against the mouse's bias: per context (pType) track its right-choice
            // rate (avgRight, an EMA) and cue the less-likely side. pType is the previous
            // (choice, rewarded?): 0/1 = left un/rewarded, 2/3 = right un/rewarded.
            if (iCue == Choices.None)
            {
                iCue = (Choices)rnd.Next(1, 3);
            }
            else{
                if (pType >= 0){
                    // iChoice-1: 0 = left, 1 = right
                    avgRight[pType] = choiceEmaAlpha * ((int)iChoice - 1) + (1 - choiceEmaAlpha) * avgRight[pType];
                }
                pType = ((int)iChoice - 1) * 2 + iSuccess;
                iCue = (rnd.NextDouble() >= avgRight[pType]) ? Choices.Right : Choices.Left;
            }
        }
        
        private void CueOn(string p = "")
        {
            if (task.Contains("Beacon"))
            {
                if (iCue == Choices.Left)
                {
                    vr.Move("beacon", new Vector3(-1.5f, 1.65f, 4.5f));
                }
                else if (iCue == Choices.Right)
                {
                    vr.Move("beacon", new Vector3(1.5f, 1.65f, 4.5f));
                }

                // EasyBeacon: block the uncued side (left cue blocks right, and vice versa).
                if (p == "e")
                {
                    if (iCue == Choices.Left)
                    {
                        vr.Move("_blockerl_p_", new Vector3(1.0f, 0.5f, 3.85f));
                        vr.Move("doorr", new Vector3(1.5f, 1.5f, 4.5f));
                    }
                    else
                    {
                        vr.Move("_blockerr_p_", new Vector3(-1.0f, 0.5f, 3.85f));
                        vr.Move("doorl", new Vector3(-1.5f, 1.5f, 4.5f));
                    }
                }
            }
        }

        private void CueOff(string p = "")
        {
            if (task.Contains("Beacon"))
            {
                // Drop the beacon below the floor during the delay so it can't leak the side.
                vr.Move("beacon", new Vector3(0f, -5f, 4.5f));

                // EasyBeacon: drop both doors and blockers; CueOn re-raises one at the cue.
                // Reset both, or last trial's door stays up.
                if (p == "e")
                {
                    vr.Move("_blockerl_p_", new Vector3(1.0f, -10f, 3.85f));
                    vr.Move("_blockerr_p_", new Vector3(-1.0f, -10f, 3.85f));
                    vr.Move("doorr", new Vector3(1.5f, -10f, 4.5f));
                    vr.Move("doorl", new Vector3(-1.5f, -10f, 4.5f));
                }
            }
        }

        public void Reward()
        {
            if (_isOpen)
            {
                // Send message to Teensy to give the reward
                serial.Write("w");
                iReward += rewardAmount;
            }
        }

        public void FlushWater() {
            if (_isOpen) {
                serial.Write("i"); // open the water valve for 1 second (teensy 'i')
                Debug.Log("Flush water for 1 second");
            }
        }

        public void SetRewardDuration()
        {
            if (_isOpen) {
                serial.Write("v" + rewardDuration + "\n");  // teensy 'v' = valve open duration in ms
                Debug.Log("Reward: " + rewardAmount + " ul / " + rewardDuration + " ms");
            }
        }

        // Teensy '0': turn every output off (a full reset on the new firmware).
        public void ResetOutputs()
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
            ResetOutputs();
            if (_isOpen)
            {
                serial.Close();
            }
            if (socket != null) socket.OnDisable();
        }
        
        public void Quit()
        {
            if (_isOpen)
            {
                serial.Write("e");
            }
            // This is basically the same as clicking the stop button
            #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
            #elif UNITY_STANDALONE
                Application.Quit();
            #endif
        }

        private void LogTrial()
        {
            taskLog.iState = iState;
            taskLog.iTrial = iTrial;
            taskLog.iTrial1 = iTrial1;
            taskLog.iTrial2 = iTrial2;
            taskLog.iCorrect = iCorrect;
            taskLog.iCorrect1 = iCorrect1;
            taskLog.iCorrect2 = iCorrect2;
            taskLog.iChoice = iChoice;
            taskLog.iCue = iCue;
            taskLog.iReward = iReward;
            taskLog.note = note;
            Logger.Log(taskLog);
        }

        private void PrintLog()
        {
            string output = "";
            if (task.Contains("Beacon") && iTrial>0)
            {
                output += iCorrect + "/" + iTrial + " (" + (100.0*iCorrect/iTrial).ToString("0") + "%)" + ", (L: " + iTrial1 + "/R: " + iTrial2 + ")";
            }
            else if (task == "Linear" && iTrial > 0)
            {
                output += iTrial;
            }
            output += ", " + iReward + " ul, " + (Time.time / 60).ToString("0.0") + " min";
            Debug.Log(output);
        }

        private void LogParameter()
        {
            taskParametersLog.animalName = animalName;
            taskParametersLog.task = task;
            taskParametersLog.nTrial = nTrial;
            taskParametersLog.rewardAmount = rewardAmount;
            taskParametersLog.rewardDuration = rewardDuration;
            taskParametersLog.note = note;
            Logger.Log(taskParametersLog);
        }

        // Log for every trial
        [Serializable]
        private class TaskLog : Logger.Entry
        {
            public States iState;
            public int iTrial;
            public int iTrial1;
            public int iTrial2;
            public int iCorrect;
            public int iCorrect1;
            public int iCorrect2;
            public Choices iChoice; // 1: left, 2: right
            public Choices iCue; // Beacon cue
            public int iReward; // total reward amount in uL
            public string note;
        }; private TaskLog taskLog = new TaskLog();

        // Log for parameters
        [Serializable]
        private class TaskParametersLog : Logger.Entry
        {
            public string animalName;
            public string task;
            public int nTrial;
            public int rewardAmount; // reward amount per trial (uL)
            public int rewardDuration; // valve-open duration per reward (ms)
            public string note;
        }; private TaskParametersLog taskParametersLog = new TaskParametersLog();

        private bool _isOpen = false;
        private bool _sessionStarted = false; // session-start 's' sent once
    }
}
