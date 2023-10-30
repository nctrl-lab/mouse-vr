using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace Janelia
{
    public class TaskManager : EditorWindow
    {
        // Parameters
        string notes = "";

        // Animal
        string[] animalList;
        int animalIndex = 0;
        string animalListFile = Path.Join(Application.dataPath, "animalList.csv");

        // Task
        string[] taskList;
        int taskIndex = 0;
        string taskListFile = Path.Join(Application.dataPath, "taskList.csv");

        float delayDurationStart = 10f;
        float delayDurationMean = 30f;
        float delayDurationEnd = 50f;
        float punishmentLatency = 1.5f;
        float punishmentDuration = 0f;
        float punishmentLength = 10f;

        string comPortPixArt = "COM3";
        string comPortReward = "COM4";
        int nTrial = 100, rewardAmountUl = 10;
        bool allowRotationYaw = false;
        bool allowRotationRoll = false;
        // bool followPath = false;
        bool reverseDirection = false;
        bool logTreadmill = true;
        bool enableKeyboard = false;
        float maxRotationSpeed = 360.0f;
        // float pathRotationMix = 0.2f;
        float pitchScale = 0.9f;
        float rollScale = 0.0f;
        float yawScale = 0.0f;
        float forwardMultiplier = 1f;
        float sideMultiplier = 1f;


        [MenuItem("Window/MouseVR")]
        public static void ShowWindow()
        {
            if (window == null)
            {
                window = GetWindow<TaskManager>("Task manager", true);
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical();

            // Control buttons
            GUILayout.Label("Configuration", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Setup"))
                Setup();
            // Set multi screen
            if (GUILayout.Button("Set screen"))
                FullScreenViewManager.ShowWindow();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(20);

            // Setup task parameters here
            if (!File.Exists(animalListFile))
            {
                using (StreamWriter file = new StreamWriter(animalListFile))
                {
                    file.Write("ANM001,ANM002");
                }
            }
            using (StreamReader reader = File.OpenText(animalListFile))
            {
                string line = reader.ReadLine();
                animalList = line.Split(',');
            }

            if (!File.Exists(taskListFile))
            {
                using (StreamWriter file = new StreamWriter(taskListFile))
                {
                    file.Write("avoidance,gonogo");
                }
            }
            using (StreamReader reader = File.OpenText(taskListFile))
            {
                string line = reader.ReadLine();
                taskList = line.Split(',');
            }

            GUILayout.Label("Task parameters", EditorStyles.boldLabel);
            animalIndex = EditorGUILayout.Popup("Animal name", animalIndex, animalList);
            taskIndex = EditorGUILayout.Popup("Task type", taskIndex, taskList);
            nTrial = EditorGUILayout.IntField("Total trial number", nTrial);
            delayDurationStart = EditorGUILayout.FloatField("Delay duration min", delayDurationStart);
            delayDurationMean = EditorGUILayout.FloatField("Delay duration mean", delayDurationMean);
            delayDurationEnd = EditorGUILayout.FloatField("Delay duration max", delayDurationEnd);
            punishmentLatency = EditorGUILayout.FloatField("Air puff latency", punishmentLatency);
            punishmentDuration = EditorGUILayout.FloatField("Air puff duration", punishmentDuration);
            punishmentLength = EditorGUILayout.FloatField("Cue length", punishmentLength);
            rewardAmountUl = EditorGUILayout.IntField("Reward amount (uL)", rewardAmountUl);

            comPortReward = EditorGUILayout.TextField("COM Port Reward", comPortReward);
            comPortPixArt = EditorGUILayout.TextField("COM Port PixArt", comPortPixArt);

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Notes", GUILayout.MaxWidth(80));
            notes = EditorGUILayout.TextArea(notes, GUILayout.Height(40));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(20);

            GUILayout.Label("Ball parameters", EditorStyles.boldLabel);
            reverseDirection = EditorGUILayout.Toggle("Reverse direction", reverseDirection);
            allowRotationRoll = EditorGUILayout.Toggle("Allow rotation by roll", allowRotationRoll);
            allowRotationYaw = EditorGUILayout.Toggle("Allow rotation by yaw", allowRotationYaw);
            maxRotationSpeed = EditorGUILayout.FloatField("Max rotation speed (degree/s)", maxRotationSpeed);
            // followPath = EditorGUILayout.Toggle("Follow path", followPath);
            // pathRotationMix = EditorGUILayout.FloatField("Ratio btw auto and manual rotation", pathRotationMix);
            enableKeyboard = EditorGUILayout.Toggle("Enable Keyboard", enableKeyboard);
            logTreadmill = EditorGUILayout.Toggle("Log treadmill", logTreadmill);
            pitchScale = EditorGUILayout.FloatField("Pitch scale (degree/pixel)", pitchScale);
            rollScale = EditorGUILayout.FloatField("Roll scale (degree/pixel)", rollScale);
            yawScale = EditorGUILayout.FloatField("Yaw scale (degree/pixel)", yawScale);
            forwardMultiplier = EditorGUILayout.FloatField("Forward multiplier", forwardMultiplier);
            sideMultiplier = EditorGUILayout.FloatField("Side multiplier", sideMultiplier);
            
            EditorGUILayout.Space(20);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Ready"))
                Ready();
            if (GUILayout.Button("Start"))
                Start();
            if (GUILayout.Button("Stop"))
                Stop();
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
        }

        private void OnDestroy() {
        }

        private void Setup()
        {
            // Player
            player = GameObject.Find("Player");
            if (player == null)
            {
                player = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                player.name = "Player";
            }
            player.transform.localPosition = new Vector3(0, 0.2f, 0);
            player.transform.localScale = new Vector3(0.3f, 0.1f, 0.3f);
            EditorUtility.SetDirty(player);

            forceRenderRate = player.GetComponent<ForceRenderRate>();
            if (forceRenderRate == null)
                forceRenderRate = player.AddComponent<ForceRenderRate>();
            forceRenderRate.rateHz = 60f;
            forceRenderRate.reportAverageRate = false;
            
            playerController = player.GetComponent<PlayerController>();
            if (playerController == null)
                playerController = player.AddComponent<PlayerController>();

            taskController = player.GetComponent<TaskController>();
            if (taskController == null)
                taskController = player.AddComponent<TaskController>();

            taskController.animalName = animalList[animalIndex];
            taskController.task = taskList[taskIndex];
            taskController.nTrial = nTrial;
            taskController.delayDurationStart = delayDurationStart;
            taskController.delayDurationMean = delayDurationMean;
            taskController.delayDurationEnd = delayDurationEnd;
            taskController.punishmentLatency = punishmentLatency;
            taskController.punishmentDuration = punishmentDuration;
            taskController.punishmentLength = punishmentLength;
            taskController.note = notes;

            // Player camera
            mainCamera = GameObject.Find("Main Camera");
            if (mainCamera == null)
                mainCamera = new GameObject("Main Camera");
            mainCamera.transform.SetParent(player.transform);
            mainCamera.transform.localPosition = Vector3.zero;
            mainCamera.transform.localRotation = Quaternion.identity;
            mainCamera.transform.localScale = new Vector3(1, 1, 1);
            Camera camera = mainCamera.GetComponent<Camera>();
            if (camera == null)
                camera = mainCamera.AddComponent<Camera>();
            camera.targetDisplay = 0;
            camera.nearClipPlane = 0.1f;

            // Light
            mainLight = GameObject.Find("Directional Light"); // Although this will not be directional, this is the default unity light.
            if (mainLight == null)
                mainLight = new GameObject("Directional Light");
            mainLight.transform.SetParent(player.transform);
            mainLight.transform.localPosition = new Vector3(0, 1, -1);
            mainLight.transform.localRotation = Quaternion.Euler(90, 0, 0);
            mainLight.transform.localScale = new Vector3(1, 1, 1);

            // Lighting setting to dark
            Material material = new Material(Shader.Find("Standard"));
            RenderSettings.skybox = material;

            Light light = mainLight.GetComponent<Light>();
            if (light == null)
                light = mainLight.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.white;
            light.range = 400;
            light.shadows = LightShadows.None;

            // Environment
            environment = GameObject.Find("Environment");
            if (environment == null)
            {
                environment = new GameObject("Environment");
                environment.transform.position = Vector3.zero;
            }
            if (environment.GetComponent<EnvironmentController>() == null)
                environment.AddComponent<EnvironmentController>();

            // Camera for multi-screen
            // if (GameObject.Find("MouseCamera3") == null)
            SetupCamerasNGon.ShowWindow();

            Debug.Log("Setup done!");
        }

        private void Ready()
        {
            player = GameObject.Find("Player");

            taskController = player.GetComponent<TaskController>();
            taskController.animalName = animalList[animalIndex];
            taskController.task = taskList[taskIndex];
            taskController.nTrial = nTrial;
            taskController.note = notes;
            taskController.comPort = comPortReward;

            playerController = player.GetComponent<PlayerController>();
            playerController.allowRotationYaw = allowRotationYaw;
            playerController.allowRotationRoll = allowRotationRoll;
            playerController.reverseDirection = reverseDirection;
            playerController.maxRotationSpeed = maxRotationSpeed;
            playerController.enableKeyboard = enableKeyboard;
            // playerController.pathRotationMix = pathRotationMix;
            // playerController.followPath = followPath;
            playerController.logTreadmill = logTreadmill;
            playerController.pitchScale = pitchScale;
            playerController.rollScale = rollScale;
            playerController.yawScale = yawScale;
            playerController.forwardMultiplier = forwardMultiplier;
            playerController.sideMultiplier = sideMultiplier;

            // Start application
            UnityEditor.EditorApplication.isPlaying = true;

            Vr.BlankDisplay(true);
            Vr.Connect(false);
        }

        private void Start()
        {
            Vr.BlankDisplay(false);
            Vr.Connect(true);

            taskController = player.GetComponent<TaskController>();
            taskController.iState = TaskController.States.Start;
        }

        private void Stop()
        {
            UnityEditor.EditorApplication.isPlaying = false;
        }

        GameObject player, environment, mainCamera, mainLight;

        private static TaskManager window;
        TaskController taskController;
        PlayerController playerController;
        ForceRenderRate forceRenderRate;
    }
}
