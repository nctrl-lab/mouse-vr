using System;
using System.IO.Ports;
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
        public int nTrial, iTrial, iTrial1, iTrial2;
        public int iCorrect, iCorrect1, iCorrect2;

        public States iState;
        public Choices iChoice, iCue;
        private int iCueRepeat, maxCueRepeat = 3;
        public int pType = -1;
        public double choiceEmaAlpha = 0.3;              // EMA smoothing for the per-context right-choice estimate (0..1)
        public double[] avgRight = {0.5, 0.5, 0.5, 0.5}; // EMA of right-choice tendency per pType; starts balanced
        public int iSucess = 0;

        public int iReward, rewardAmount = 10;   // uL per reward (from calibration); drives the iReward/rewardMax cap
        public int rewardDuration = 60;          // valve-open time in ms (from calibration); sent to the Teensy
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

        // Shared RNG. A fresh System.Random() is seeded from the system clock, so
        // constructing one per call can yield correlated/identical draws when two
        // calls land in the same clock tick. Use a single instance instead.
        private System.Random rnd = new System.Random();

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

            LogParameter(); // Log task parameters (animal name, task, trial number, reward amount per trial)
            Reset(); // Reset trial-related variables

            SetRewardDuration();

            vr.Start(); // This gets the list of object that needs to be controlled during task.
        }

        private void Update()
        {
            if (iState == States.Start)
            {
                try {
                    note = "start";
                    Vr.BlankDisplay(false); // a trial is beginning: show the scene
                    Invoke(task, 0f);
                }
                catch (Exception e)
                {
                    Debug.Log("Update Error: " + e);
                }
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
            iTrial1 = 0;
            iTrial2 = 0;
            iCorrect1 = 0;
            iCorrect2 = 0;
            iChoice = Choices.None;
            iCue = Choices.None;
            iReward = 0;
            note = "";

            // Beacon countermeasure state (see nextCueCounter): start each session unbiased.
            pType = -1;
            iSucess = 0;
            for (int i = 0; i < avgRight.Length; i++)
                avgRight[i] = 0.5;
        }

        // True while the session should keep running: trial budget and reward cap not yet reached.
        private bool SessionRunning => iTrial < nTrial && iReward < rewardMax;

        //////////////////////////////////////////////////////////////////////////////////////////////////////////
        public void Linear_A()
        {
            /// linear task ///
            // 1. Start: teleport animal
            if (note == "start")
            {
                iTrial++;
                iState = States.Delay;
                vr.Teleport("ls"); // teleport to start position
                LogTrial();
            }
            else if (note.StartsWith("lr")) // reward
            {
                if (iState == States.Delay) {
                    iState = States.Choice;
                    Reward();
                    LogTrial();
                }
                else {
                    iState = States.Other;
                }
            }
            else if (note.StartsWith("le")) // End
            {
                // Stop current trial and restart
                CancelInvoke();
                if (SessionRunning)
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
        // Shared Zigzag-A (alternation) implementation. p is the environment-token prefix:
        //   "s" = superEasy, "e" = easy, "z" = Zigzag_A.
        // Tokens: start->p+"s", delay-end->p+"es", left->p+"l", right->p+"r", end->p+"e".
        private void ZigzagA(string p)
        {
            if (note == "start")
            {
                if (iCue == Choices.None) {
                    NextCue(2);
                }
                else if (iCue == Choices.Left) {
                    iCue = Choices.Right;
                }
                else if (iCue == Choices.Right) {
                    iCue = Choices.Left;
                }
                iState = States.Delay;
                iTrial++;
                Debug.Log("iCue: " + iCue);
                vr.Teleport(p + "s");
                LogTrial();
            }
            else if (note.StartsWith(p + "es") && iState == States.Delay) // delay end cue on
            {
                iState = States.Choice;
                if (iCue == Choices.Left) {
                    vr.Teleport(p + "l");
                }
                else if (iCue == Choices.Right) {
                    vr.Teleport(p + "r");
                }
                LogTrial();
            }
            else if (note.StartsWith(p + "l") && iState == States.Choice) // left choice
            {
                iState = States.Success;
                Reward();
                iCorrect++;
                iTrial1++;
                LogTrial();
            }
            else if (note.StartsWith(p + "r") && iState == States.Choice) // right choice
            {
                iState = States.Success;
                Reward();
                iCorrect++;
                iTrial2++;
                LogTrial();
            }
            else if (note.StartsWith(p + "e") && iState == States.Success) // trial end
            {
                CancelInvoke();
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
        }
        public void Zigzag_A_superEasy() { ZigzagA("s"); }
        public void Zigzag_A_easy() { ZigzagA("e"); }
        public void Zigzag_A() { ZigzagA("z"); }
        
        public void Alternation()
        {
            /// Alternation task ///
            // 1. Start: teleport animal / place reward cue
            if (note == "start")
            {
                if (_isOpen)
                {
                    serial.Write("s"); // session start (trigger on; teensy ignores after the first)
                    serial.Write("S"); // trial start (delay start)
                }
                iState = States.Delay;
                iTrial++;
                if (iChoice == Choices.Left) {
                    iCue = Choices.Right;
                }
                else if (iChoice == Choices.Right) {
                    iCue = Choices.Left;
                }
                Debug.Log("iCue: " + iCue);
                vr.Teleport("as");
                LogTrial();
            }
            else if (note.StartsWith("aes") && iState == States.Delay) // delay end
            {
                if (_isOpen)
                {
                    // cue start: mark the rewarded side (L/R) for recording
                    if (iCue == Choices.Left)
                        serial.Write("L");
                    else if (iCue == Choices.Right)
                        serial.Write("R");
                }
                iState = States.Choice;
                vr.Teleport("at");
                LogTrial();
            }
            // 2. Target:
            else if (note.StartsWith("ar"))
            {
                if (iState == States.Choice)
                {
                    if (_isOpen)
                    {
                        serial.Write("r"); // right
                    }
                    Debug.Log("Right");
                    if (iCue == Choices.Right || iCue == Choices.None)
                    {
                        iState = States.Success;
                        iCorrect++;
                        iCorrect2++;
                        Reward();
                        Debug.Log("=================== SUCCESS ===================");
                    }
                    else{
                        iState = States.Failure;
                    }
                    iTrial2++;
                    iChoice = Choices.Right;
                }
                else {
                    iState = States.Other;
                }
                LogTrial();
            }
            else if (note.StartsWith("al"))
            {
                if (iState == States.Choice)
                {
                    if (_isOpen)
                    {
                        serial.Write("l"); // left
                    }
                    Debug.Log("Left" );
                    if (iCue == Choices.Left || iCue == Choices.None)
                    {
                        iState = States.Success;
                        iCorrect++;
                        iCorrect1++;
                        Reward();
                        Debug.Log("=================== SUCCESS ===================");
                    }
                    else{
                        iState = States.Failure;
                    }
                    iChoice = Choices.Left;
                    iTrial1++;
                }
                else {
                    iState = States.Other;
                }
                LogTrial();
            }
            else if (note.StartsWith("ae0") || note.StartsWith("ae1")) // End
            {
                // Stop current trial and restart
                CancelInvoke();
                if (SessionRunning)
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
        /// /////////////////////////////////////////////////////////////////////////////////////////
        public void Linear_B()
        {
            /// linear task ///
            // 1. Start: teleport animal to start position
            if (note == "start")
            {
                iState = States.Delay;
                vr.Teleport("ls");
                iTrial++;
                LogTrial();
            }
            else if (note.StartsWith("le")) // delay end
            {
                iState = States.Choice;
                vr.Teleport("lc");
                LogTrial();
            }
            // 2. Target:
            else if (note.StartsWith("lo")) // trial end & reward
            {
                if (iState == States.Choice)
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
                    StartCoroutine(Blackout(successITI)); // black screen for 2sec
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
        // Shared Zigzag-B implementation. p is the environment-token prefix:
        //   "g" = Zigzag_B_easy, "z" = Zigzag_B.
        // Tokens: start->p+"s", delay-end->p+"e", left->p+"l", right->p+"r", outcome->p+"o".
        private void ZigzagB(string p)
        {
            // 1. Start: teleport animal
            if (note == "start")
            {
                NextCue(2);
                iState = States.Delay;
                iTrial++;
                Debug.Log("cue: " + iCue);
                vr.Teleport(p + "s");
                LogTrial();
            }
            else if (note.StartsWith(p + "e")) // delay end
            {
                iState = States.Choice;
                if (iCue == Choices.Left)
                {
                    vr.Teleport(p + "l");
                }
                else if (iCue == Choices.Right)
                {
                    vr.Teleport(p + "r");
                }
                LogTrial();
            }
            else if (note.StartsWith(p + "o")) // trial end & reward
            {
                if (iState == States.Choice)
                {
                    if (iCue == Choices.Left) // left trial end
                    {
                        iState = States.Success;
                        iCorrect++;
                        iCorrect1++;
                        iTrial1++;
                        iChoice = Choices.Left;
                        Reward();
                        Debug.Log("=================== SUCCESS ===================");
                    }
                    else if (iCue == Choices.Right) // Right trial end
                    {
                        iState = States.Success;
                        iCorrect++;
                        iCorrect2++;
                        iChoice = Choices.Right;
                        iTrial2++;
                        Reward();
                        Debug.Log("=================== SUCCESS ===================");
                    }
                    else{
                        iState = States.Failure;
                    }
                    CancelInvoke();
                    LogTrial();
                    if (SessionRunning)
                    {
                        StartCoroutine(Blackout(successITI));
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
                else
                {
                    iState = States.Other;
                }
            }
        }
        public void Zigzag_B_easy() { ZigzagB("g"); }
        public void Zigzag_B() { ZigzagB("z"); }

        public void Beacon()
        {
            /// Beacon task ///
            if (note == "start")
            {
                if (_isOpen)
                {
                    serial.Write("s"); // session start (trigger on; teensy ignores after the first)
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
                CueOn();
                LogTrial();
            }
            else if (note.StartsWith("left") || note.StartsWith("right")) // animal reached a side
            {
                if (iState != States.Choice)
                {
                    iState = States.Other;
                    return;
                }

                // note reports the animal's chosen side; correct iff it matches the cue.
                iChoice = note.StartsWith("left") ? Choices.Left : Choices.Right;
                bool correct = iChoice == iCue;
                if (correct)
                {
                    iState = States.Success;
                    iSucess = 1;
                    iCorrect++;
                    ITI = successITI;
                    Reward();
                }
                else
                {
                    iState = States.Failure;
                    iSucess = 0;
                    ITI = failureITI;
                }

                RecordChoice(iChoice, correct);
                EndBeaconTrial();
            }
        }

        // Tally a Beacon choice: teensy 'l'/'r', the per-side trial count, and (on
        // success) the per-side correct count.
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

        // Common end-of-trial handling for Beacon: stop the pending re-Invoke, log the
        // outcome, then run the ITI blackout (which begins the next trial) or quit once
        // the trial/reward budget is exhausted.
        private void EndBeaconTrial()
        {
            CueOff();
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
            iState = States.Start; // next trial; Update() unblanks when it begins
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

        public void NextCue(int nCue)
        {
            if (task.StartsWith("Zigzag"))
            {
                if (iCue == Choices.None)
                    iCue = (Choices)rnd.Next(1, nCue + 1);
                else
                    iCue = (Choices)DrawCue((int)iCue, nCue);
            }
        }

        // Draw a cue index in [1, nCue]; if it keeps repeating the previous value past
        // maxCueRepeat times, force a different index. Used by the Zigzag cue draw in NextCue.
        private int DrawCue(int prev, int nCue)
        {
            int next = rnd.Next(1, nCue + 1);
            if (next == prev) {
                iCueRepeat++;
                if (iCueRepeat > maxCueRepeat) {
                    int rndidx = rnd.Next(1, nCue);
                    if (rndidx >= prev)
                        rndidx++;
                    next = rndidx;
                    iCueRepeat = 0;
                }
            }
            return next;
        }
        public void nextCueCounter()
        {
            // beacon cue selection by mice behavior (countermeasure strategy).
            // Per context (pType) track the animal's right-choice tendency as an
            // exponential moving average (avgRight), then cue the side it is LESS
            // likely to pick: P(cue = Right) = 1 - avgRight[pType].
            // pType encodes the previous (choice, rewarded?) — the situation that
            // drives win-stay / lose-shift behavior:
            // 0: choice=left,  unrewarded
            // 1: choice=left,  rewarded
            // 2: choice=right, unrewarded
            // 3: choice=right, rewarded
            if (iCue == Choices.None)
            {
                iCue = (Choices)rnd.Next(1, 3);
            }
            else{
                if (pType >= 0){
                    // iChoice-1: 0 = left, 1 = right
                    avgRight[pType] = choiceEmaAlpha * ((int)iChoice - 1) + (1 - choiceEmaAlpha) * avgRight[pType];
                }
                pType = ((int)iChoice - 1) * 2 + iSucess;
                iCue = (rnd.NextDouble() >= avgRight[pType]) ? Choices.Right : Choices.Left;
            }
        }
        
        private void CueOn()
        {
            if (task.StartsWith("Beacon"))
            {
                if (iCue == Choices.Left)
                {
                    vr.Move("beacon", new Vector3(-1.5f, 2.4f, 4.5f));
                }
                else if (iCue == Choices.Right)
                {
                    vr.Move("beacon", new Vector3(1.5f, 2.4f, 4.5f));
                }
            }
        }

        private void CueOff()
        {
            if (task.StartsWith("Beacon"))
            {
                // Hide the beacon below the floor during the delay so it doesn't
                // leak the cued side before CueOn() reveals it at delay end.
                vr.Move("beacon", new Vector3(0f, -5f, 4.5f));
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
            socket.OnDisable();
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
            if ((task == "Beacon" || task == "Zigzag_B" || task == "Zigzag_B_easy" || task == "Alternation" || task == "Zigzag_A" || task == "Zigzag_A_easy" || task == "Zigzag_A_superEasy") && iTrial>0)
            {
                output += iCorrect + "/" + iTrial + " (" + (100*iCorrect/iTrial).ToString("0") + "%)" + ", (L: " + iTrial1 + "/R: " + iTrial2 + ")";
            }
            else if ((task == "Linear_A" || task == "Linear_B" )&& iTrial > 0)
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
    }
}
