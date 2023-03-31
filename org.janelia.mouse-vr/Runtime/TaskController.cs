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

        private void OnDisable()
        {
            if (_isOpen)
            {
                serial.Close();
            }
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
