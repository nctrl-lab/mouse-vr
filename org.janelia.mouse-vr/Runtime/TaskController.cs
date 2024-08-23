using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

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
        public Cues pCue, iCue;
        public Choices iChoice, pChoice, cChoice;
        private int iCueRepeat, maxCueRepeat = 3;
        public int cueRatio = 50;
        public bool result;
        public int pType = -1;
        public int nTrialToWatch = 3;
        public int[] sumRight = {0,0,0,0};
        public int[] recentChoice = {0b00000000, 0b00000000, 0b00000000, 0b00000000};
        public int iSucess = 0;

        public int iReward, rewardAmount = 10;
        public int rewardMax = 1500;

        private float delayDuration;
        public float delayDurationStart = 30f;
        public float delayDurationMean = 60f;
        public float delayDurationEnd = 120f;

        public float rewardLatency = 1.0f;
        public float rewardLatencyMin = 0.0f;
        public float rewardLatencyMax = 3.0f;
        public float rewardLatencyUp = 0.05f;
        public float rewardLatencyDown = -0.05f;

        public float punishmentLatency = 4f;
        public float punishmentLatencyMin = 2f;
        public float punishmentLatencyMax = 5f;
        public float punishmentLatencyUp = 0.05f;
        public float punishmentLatencyDown = -0.05f;
        public float punishmentDuration = 10f; // infinite if zero

        // Beacon ITI
        public float ITI = 2.0f;
        public float successITI = 2.0f;
        public float failureITI = 10.0f;

        // Serial ports to Teensy (or BCS) to give reward (or optogenetics)
        public string comPort = "COM8";
        public SerialPort serial;
        public bool sendSlackNotification = true;
        

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

        // Slack
        string slackFile = Path.Join(Application.dataPath, "slackUri.txt");
        string slackUri = "";

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
            // Debug.Log("start");
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

            // Slack
            if (File.Exists(slackFile))
            {
                using (StreamReader reader = File.OpenText(slackFile))
                {
                    slackUri = reader.ReadLine();
                }
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
            // Debug.Log("iState: " + iState);
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
            iCue = Cues.None;
            pCue = Cues.None;
            iChoice = Choices.None;
            pChoice = Choices.None;
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
                iTrial1++;
                CueOn();
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

        private void NogoGoLearn()
        {
            // 1. Start: teleport animal / place reward cue
            if (note == "start")
            {
                iState = States.Delay;

                if (cueRatio == 50) {
                    NextCue(2);
                }
                else {
                    NextCueRatio();
                }

                iTrial++;
                if (iCue == Cues.Nogo) {
                    iTrial1++;
                }
                else {
                    iTrial2++;
                }
                CueOn();
                LogTrial();
            }
            // 2. Choice: wait until to touch target
            //  1) No-go: a reward is given after rewardLatency seconds
            //  2) Go: punishment is given after punishmentLatency seconds
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
            // 3. Target:
            //  1) No-go: failure if the animal touches this before rewardLatency has passed
            //  2) Go: success if the animal touches this before punishmentLatency has passed
            else if (note.StartsWith("target"))
            {
                if (iState == States.Choice)
                {
                    if (iCue == Cues.Nogo) {
                        iState = States.Failure;
                        result = false;
                    }
                    else if (iCue == Cues.Go) {
                        iState = States.Success;
                        result = true;
                        iCorrect++;
                        iCorrect2++;
                    }
                    Debug.Log(iState);
                }
                else {
                    iState = States.Other;
                }
                LogTrial();
                PunishmentOff();
                // No response if outcome already happened.
            }
            // 4. End: check session finishing condition
            else if (note.StartsWith("end"))
            {
                // Stop current trial and restart
                CancelInvoke();
                CueOff();
                if ((iTrial < nTrial) && (iReward < rewardMax))
                {
                    CheckLatency();
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

        private void NogoGo()
        {
            // 1. Start: teleport animal / place reward cue
            if (note == "start")
            {
                iState = States.Delay;

                if (cueRatio == 50) {
                    NextCue(2);
                }
                else {
                    NextCueRatio();
                }

                iTrial++;
                if (iCue == Cues.Nogo) {
                    iTrial1++;
                }
                else {
                    iTrial2++;
                }
                CueOn();
                LogTrial();
            }
            // 2. Choice: wait until to touch target
            //  1) No-go: a reward is given after rewardLatency seconds
            //  2) Go: punishment is given after punishmentLatency seconds
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
            // 3. Target:
            //  1) No-go: failure if the animal touches this before rewardLatency has passed
            //  2) Go: success if the animal touches this before punishmentLatency has passed
            else if (note.StartsWith("target"))
            {
                if (iState == States.Choice)
                {
                    if (iCue == Cues.Nogo) {
                        iState = States.Failure;
                        result = false;
                    }
                    else if (iCue == Cues.Go) {
                        iState = States.Success;
                        result = true;
                        iCorrect++;
                        iCorrect2++;
                    }
                    Debug.Log(iState);
                }
                else {
                    iState = States.Other;
                }
                LogTrial();
                PunishmentOff();
                // No response if outcome already happened.
            }
            // 4. End: check session finishing condition
            else if (note.StartsWith("end"))
            {
                // Stop current trial and restart
                CancelInvoke();
                CueOff();
                if ((iTrial < nTrial) && (iReward < rewardMax))
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
        //////////////////////////////////////////////////////////////////////////////////////////////////////////
        public void Alter()
        {
            /// Alternative task ///
            // 1. Start: teleport animal / place reward cue
            if (note == "start")
            {
                if (_isOpen)
                {
                    serial.Write("s"); // start
                }
                // Debug.Log("start");
                iState = States.Choice;
                iTrial++;
                // if (iChoice == Choices.Left) {
                //     iTrial1++;
                // }
                // else if (iChoice == Choices.Right) {
                //     iTrial2++;
                // }
                Debug.Log("iChoice: " + iChoice);
                CueOn(); // Transport
                LogTrial();
            }
            // 2. Target:
            else if (note.StartsWith("r0"))
            {
                if (iState == States.Choice)
                {
                    if (_isOpen)
                    {
                        serial.Write("r"); // right
                    }
                    // Debug.Log("Right");
                    if (iChoice == Choices.Right || iChoice == Choices.None)
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
                    iChoice = Choices.Left;
                    iTrial2++;
                }
                else {
                    iState = States.Other;
                }
                LogTrial();
                // Debug.Log("iState: " + iState);
            }
            else if (note.StartsWith("l0"))
            {
                if (iState == States.Choice)
                {
                    if (_isOpen)
                    {
                        serial.Write("l"); // left
                    }
                    // Debug.Log("Left" );
                    if (iChoice == Choices.Left || iChoice == Choices.None)
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
                    iChoice = Choices.Right;
                    iTrial1++;
                }
                else {
                    iState = States.Other;
                }
                LogTrial();
                // Debug.Log("iState: " + iState);
            }
            else if (note.StartsWith("e")) // End
            {
                if (_isOpen)
                {
                    serial.Write("e"); // end
                }
                // Stop current trial and restart
                CancelInvoke();
                // CueOff();
                if ((iTrial < nTrial) && (iReward < rewardMax))
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
        public void Linear_B()
        {
            /// linear task ///
            // 1. Start: teleport animal / place reward cue
            if (note == "start")
            {
                // Debug.Log("start State " + iState);
                iState = States.Delay;
                if (cueRatio == 50) {
                    NextCue(2);
                }
                else {
                    NextCueRatio();
                }

                iTrial++;
                CueOn(); // Transport
                LogTrial();
            }
            else if (note.StartsWith("e1")) // delay end
            {
                // Debug.Log("reward State " + iState);
                iState = States.Choice;
                CueOn(); // Transport
                LogTrial();
            }
            // 2. Target:
            else if (note.StartsWith("r0")) // reward
            {
                if (iState == States.Choice)
                {
                    iState = States.Success;
                    Reward();
                    // Debug.Log("Reward");
                    LogTrial();
                }
                else {
                    iState = States.Other;
                }
            }
            else if (note.StartsWith("e2")) // End
            {
                // Stop current trial and restart
                CancelInvoke();
                // Debug.Log("end");
                // CueOff();
                if ((iTrial < nTrial) && (iReward < rewardMax))
                {
                    if (iState != States.Delay)
                    {
                        StartCoroutine(Blackout(2f)); // black screen for 2sec
                    PrintLog();
                    }
                }
                else
                {
                    iState = States.Standby;
                    LogTrial();
                    Quit();
                }
            }
        }
        public void Zigzag()
        {
            /// Alternative task ///
            // 1. Start: teleport animal / place reward cue
            if (note == "start")
            {
                if (_isOpen)
                {
                    serial.Write("s"); // start
                }
                // Debug.Log("start");
                if (cueRatio == 50) {
                    NextCue(2);
                }
                else {
                    NextCueRatio();
                }
                iState = States.Delay;
                iTrial++;
                Debug.Log("cChoice: " + cChoice);
                CueOn(); // Transport
                LogTrial();
            }
            else if (note.StartsWith("e")) // delay end or trial end
            {
                if (note.StartsWith("es")) // delay end
                {
                    iState = States.Choice;
                    CueOn(); // Transport
                    LogTrial();
                }
                else
                {
                    if (note.StartsWith("el")) // left - trial end
                    {
                        if (iState == States.Choice)
                        {
                            if (_isOpen)
                            {
                                serial.Write("l"); // left
                            }
                            iChoice = Choices.Left;
                            // Debug.Log("Left" );
                            if (cChoice == Choices.Left)
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
                            iTrial1++;
                        }
                        else {
                            iState = States.Other;
                        }
                    }
                    else if (note.StartsWith("er")) // right - trial end
                    {
                        if (iState == States.Choice)
                        {
                            if (_isOpen)
                            {
                                serial.Write("r");
                            }
                            // Debug.Log("Right" );
                            iChoice = Choices.Right;
                            if (cChoice == Choices.Right)
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
                        }
                        else {
                            iState = States.Other;
                        }
                    }
                     // Stop current trial and restart
                    CancelInvoke();
                    LogTrial();
                    // CueOff();
                    if ((iTrial < nTrial) && (iReward < rewardMax))
                    {
                        StartCoroutine(Blackout(successITI));
                        // iState = States.Start;
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
            }
        }
        public void Beacon()
        {
            /// Alternative task ///
            // 1. Start: teleport animal / place reward cue
            if (note == "start")
            {
                if (_isOpen)
                {
                    serial.Write("s"); // start
                }
                // Debug.Log("start");
                // if (cueRatio == 50) {
                //     NextCue(2);
                // }
                // else {
                //     NextCueRatio();
                // }
                nextCueCounter();
                iState = States.Delay;
                iTrial++;
                Debug.Log("cChoice: " + cChoice);
                CueOn(); // Transport
                LogTrial();
            }
            else if (note.StartsWith("e")) // delay end or trial end
            {
                if (note.StartsWith("es")) // delay end
                {
                    iState = States.Choice;
                    CueOn(); // Transport
                    LogTrial();
                }
                else
                {
                    if (note.StartsWith("el")) // left - trial end
                    {
                        if (iState == States.Choice)
                        {
                            if (_isOpen)
                            {
                                serial.Write("l"); // left
                            }
                            iChoice = Choices.Left;
                            // Debug.Log("Left" );
                            if (cChoice == Choices.Left)
                            {
                                iState = States.Success;
                                iSucess = 1;
                                iCorrect++;
                                iCorrect1++;
                                Reward();
                                ITI = successITI;
                                Debug.Log("=================== SUCCESS ===================");
                            }
                            else{
                                iState = States.Failure;
                                iSucess = 0;
                                ITI = failureITI;
                            }
                            iTrial1++;
                        }
                        else {
                            iState = States.Other;
                        }
                    }
                    else if (note.StartsWith("er")) // right - trial end
                    {
                        if (iState == States.Choice)
                        {
                            if (_isOpen)
                            {
                                serial.Write("r");
                            }
                            // Debug.Log("Right" );
                            iChoice = Choices.Right;
                            if (cChoice == Choices.Right)
                            {
                                iState = States.Success;
                                iSucess = 1;
                                iCorrect++;
                                iCorrect2++;
                                Reward();
                                ITI = successITI;
                                Debug.Log("=================== SUCCESS ===================");
                            }
                            else{
                                iState = States.Failure;
                                iSucess = 0;
                                ITI = failureITI;
                            }
                            iTrial2++;
                        }
                        else {
                            iState = States.Other;
                        }
                    }
                     // Stop current trial and restart
                    CancelInvoke();
                    LogTrial();
                    // CueOff();
                    if ((iTrial < nTrial) && (iReward < rewardMax))
                    {
                        StartCoroutine(Blackout(ITI));
                        // iState = States.Start;
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
            }
        }
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        public IEnumerator Blackout(float delay)
        {
            iState = States.Delay;
            vr.Teleport("11");
            Vr.BlankDisplay();
            yield return new WaitForSeconds(delay);
            iState = States.Start;
            Vr.BlankDisplay();
        }
        public void NextCue(int nCue)
        {
            var rnd = new System.Random();
            if (task.StartsWith("NoGo"))
            {
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
            else if (task.StartsWith("Zigzag")|| task.StartsWith("Beacon"))
            {
                if (cChoice == Choices.None) {
                    cChoice = (Choices)rnd.Next(1, nCue+1);
                }
                else {
                    pChoice = cChoice;
                    cChoice = (Choices)rnd.Next(1, nCue+1);
                    if (pChoice == cChoice) {
                        iCueRepeat++;
                        if (iCueRepeat > maxCueRepeat) {
                            int rndidx = rnd.Next(1, nCue);
                            if (rndidx >= (int)pChoice)
                                rndidx++;
                            cChoice = (Choices)rndidx;
                            iCueRepeat = 0;
                        }
                    }
                }
            }
        }
        public void NextChoice(int nChoice)
        {
            var rnd = new System.Random();
            if (iChoice == Choices.None) {
                iChoice = (Choices)rnd.Next(1, nChoice+1);
            }
            // else {
            //     pChoice = iChoice;
            //     iChoice = (Choices)rnd.Next(1, nChoice+1);
            //     if (pChoice == iChoice) {
            //         iChoiceRepeat++;
            //         if (iChoiceRepeat > maxChoiceRepeat) {
            //             int rndidx = rnd.Next(1, nChoice);
            //             if (rndidx >= (int)pChoice)
            //                 rndidx++;
            //             iChoice = (Choices)rndidx;
            //             iChoiceRepeat = 0;
            //         }
            //     }
            //     else
            //     {
            //         iChoiceRepeat = 0;
            //     }
            // }
            // Debug.Log("iChoice: " + iChoice);
        }
        public void nextCueCounter()
        {
            // beacon cue selection by mice behavior (countermeasure strategy)
            // 0: cue=1(left) choice=2(right) unrewarded -> p(next choice=R|prev choice=R) = 0 when alternate
            // 1: cue=1(left) choice=1(left) rewarded -> p(R|L) = 1
            // 2: cue=2(right) choice=1(left) unrewarded -> p(R|L) = 1
            // 3: cue=2(right) choice=2(right) rewarded -> p(R|R) = 0
            var rnd = new System.Random();
            if (cChoice == Choices.None)
            {
                cChoice = (Choices)rnd.Next(1, 3);
            }
            else{
                if (pType >= 0){
                    sumRight[pType] = sumRight[pType] + ((int)iChoice -1) - (recentChoice[pType] & 1);
                    recentChoice[pType] = (recentChoice[pType]>>1) | (((int)iChoice-1) << (nTrialToWatch - 1));
                }
                pType = ((int)cChoice -1) * 2 + iSucess;
                int conditionResult = (rnd.Next(0, nTrialToWatch) >= sumRight[(int)pType]) ? 1 : 0;
                cChoice = (Choices)(conditionResult +1);
                Debug.Log("pType" + pType);
                Debug.Log("sumRight" + sumRight[0] + sumRight[1] + sumRight[2]+ sumRight[3]);
                Debug.Log("recentChoice" + recentChoice[0]+ recentChoice[1]+ recentChoice[2]+ recentChoice[3]);
            }
        }

        public void NextCueRatio()
        {
            var rnd = new System.Random();
            if (task.StartsWith("NoGo"))
            {
                iCue = (Cues)(Convert.ToInt32(rnd.Next(100) >= cueRatio) + 1);
            }
            else if (task.StartsWith("Zigzag")||task.StartsWith("Beacon"))
            {
                cChoice = (Choices)(Convert.ToInt32(rnd.Next(100) >= cueRatio) + 1);
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
                        result = true;
                        iCorrect++;
                        iCorrect1++;
                        note = "";
                        Reward();
                        LogTrial();
                        Debug.Log(iState);
                    }
                    else if (iCue == Cues.Go)
                    {
                        iState = States.Failure;
                        result = false;
                        note = "";
                        PunishmentOn();
                        LogTrial();
                        Debug.Log(iState);
                    }
                }
                // Nothing happens if Success or Failure happend already.
            }
        }

        private void CheckLatency() {
            // modify latency depending on the cue and result
            if (iCue == Cues.Nogo) {
                if (result) {
                    rewardLatency += rewardLatencyUp;
                }
                else {
                    rewardLatency += rewardLatencyDown;
                }
                rewardLatency = Math.Clamp(rewardLatency, rewardLatencyMin, rewardLatencyMax);
            }
            else {
                if (result) {
                    punishmentLatency += punishmentLatencyDown;
                }
                else {
                    punishmentLatency += punishmentLatencyUp;
                }
                punishmentLatency = Math.Clamp(punishmentLatency, punishmentLatencyMin, punishmentLatencyMax);
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
            //Debug.Log("Delay distance: " + 10f * distance + " cm");
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
            else if (task.StartsWith("Nogo"))
            {
                Debug.Log("iChoice: " + iChoice);
            }
            else if (task.StartsWith("Alter"))
            {
                vr.Teleport("10");
            }
            else if (task.StartsWith("Linear_B"))
            {
                if (iState == States.Delay)
                {
                    vr.Teleport("10");
                }
                else if (iState == States.Choice)
                {
                    vr.Teleport("20");
                }
            }
            else if (task.StartsWith("Zigzag")||task.StartsWith("Beacon"))
            {
                if (iState == States.Delay) // start
                {
                    vr.Teleport("00");
                }
                else if (iState == States.Choice) // cue start
                {
                    if (cChoice == Choices.Left)
                    {
                        vr.Teleport("10");
                    }
                    else if (cChoice == Choices.Right)
                    {
                        vr.Teleport("01");
                    }   
                }
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
                serial.Write("w");
                iReward += rewardAmount;
                Debug.Log("Reward");
            }
        }

        public void FlushWater() {
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
                Debug.Log("Punishment duration: " + (int)punishmentDuration + " s");
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
        
        public void Quit()
        {
            if (sendSlackNotification && slackUri != "") {
                StartCoroutine(Slack());
            }
            else {
                // This is basically the same as clicking the stop button
                UnityEditor.EditorApplication.isPlaying = false;
            }
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
            // taskLog.iCue = iCue;
            taskLog.iChoice = iChoice;
            taskLog.cChoice = cChoice;
            taskLog.iReward = iReward;
            taskLog.delayDuration = delayDuration;
            taskLog.rewardLatency = rewardLatency;
            // taskLog.punishmentLatency = punishmentLatency;
            taskLog.note = note;
            Logger.Log(taskLog);
        }

        private void PrintLog()
        {
            string output = "";
            if (task == "NogoGo" || task == "Nogo" || task == "NogoGoLearn"){
                if (iTrial > 0)
                    output += iCorrect + "/" + iTrial + " (" + (100*iCorrect/iTrial).ToString("0") + "%)";
                if (iTrial1 > 0)
                    output += ", no-go:" + iCorrect1 + "/" + iTrial1 + " (" + (100*iCorrect1/iTrial1).ToString("0") + "%)";
                if (iTrial2 > 0)
                    output += ", go:" + iCorrect2 + "/" + iTrial2 + " (" + (100*iCorrect2/iTrial2).ToString("0") + "%)";
                
                if (task == "NogoGoLearn")
                    output += String.Format(", Tno-go: {0:0.##} s, Tgo: {1:0.##} s", rewardLatency, punishmentLatency);
            }
            else if (task == "Alter" && iTrial > 0)
            {
                output += iCorrect + "/" + iTrial + " (" + (100*iCorrect/iTrial).ToString("0") + "%)" + ", (L: " + iTrial1 + "/R: " + iTrial2 + ")";
                // trial, success, perf, left, right
            }
            else if ((task == "Beacon" || task == "Zigzag") && iTrial>0)
            {
                output += iCorrect + "/" + iTrial + " (" + (100*iCorrect/iTrial).ToString("0") + "%)" + ", (L: " + iTrial1 + "/R: " + iTrial2 + ")";
            }
            output += ", " + iReward + " ul, " + (Time.time / 60).ToString("0.0") + " min";
            Debug.Log(output);
        }

        IEnumerator Slack()
        {
            string payload = animalName + " (" + task + ") ";
            if (iTrial > 0)
                payload += iCorrect + "/" + iTrial + " (" + (100*iCorrect/iTrial).ToString("0") + "%)";
            if (iTrial1 > 0)
                payload += ", no-go:" + iCorrect1 + "/" + iTrial1 + " (" + (100*iCorrect1/iTrial1).ToString("0") + "%)";
            if (iTrial2 > 0)
                payload += ", go:" + iCorrect2 + "/" + iTrial2 + " (" + (100*iCorrect2/iTrial2).ToString("0") + "%)";
            payload += ", " + iReward + " ul, " + (Time.time / 60).ToString("0.0") + " min";
            payload += String.Format(", Tno-go: {0:0.##} s, Tgo: {1:0.##} s", rewardLatency, punishmentLatency);
            
            using (UnityWebRequest www = UnityWebRequest.Post(slackUri, "{'text':'" + payload + "'}", "application/json"))
            {
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.Log(www.error);
                }
                else
                {
                    Debug.Log(payload);
                }
            }
            
            // This is basically the same as clicking the stop button
            UnityEditor.EditorApplication.isPlaying = false;
        }

        private void LogParameter()
        {
            taskParametersLog.animalName = animalName;
            taskParametersLog.task = task;
            taskParametersLog.nTrial = nTrial;
            taskParametersLog.cueRatio = cueRatio;
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
            public int iTrial1;
            public int iTrial2;
            public int iCorrect;
            public int iCorrect1;
            public int iCorrect2;
            // public Cues iCue; 
            public Choices iChoice; // 1: left, 2: right
            public Choices cChoice; // Beacon cue
            public int iReward; // total reward amount in uL 
            public float delayDuration;
            public float rewardLatency;
            public float punishmentLatency;
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
            public float delayDurationStart;
            public float delayDurationMean;
            public float delayDurationEnd;
            public int cueRatio;
            public float rewardLatency;
            public float punishmentLatency;
            public float punishmentDuration; // infinite if zero
            public string note;
        }; private TaskParametersLog taskParametersLog = new TaskParametersLog();

        private bool _isOpen = false;
    }
}
