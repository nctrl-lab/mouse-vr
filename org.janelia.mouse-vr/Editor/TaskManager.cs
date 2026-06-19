using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
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

        int nTrial = 500;
        int rewardMax = 1000;

        // Reward calibration produced by Asset/teensy/calibrate.py
        string calibFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "mouse-vr", "water_calibration.json");
        CalibTarget[] rewardCalib = new CalibTarget[0];
        string[] rewardLabels = new string[0];
        int rewardIndex = 0;

        float delayDurationStart = 30f;
        float delayDurationMean = 60f;
        float delayDurationEnd = 90f;

        float punishmentLatency = 4f;
        float punishmentDuration = 10f;

        float lightIntensity = 1.0f; // flat ambient brightness (0 = black, 1 = white)

        public bool showConfig = false;
        string comPortPixArt = "COM4";
        string comPortTeensy = "COM3";
        bool allowRotationYaw = false;
        bool allowRotationRoll = false;
        // bool followPath = false;
        bool reverseDirection = false;
        bool logTreadmill = true;
        bool enableKeyboard = false;
        float maxRotationSpeed = 360.0f;
        // float pathRotationMix = 0.2f;
        float pitchScale = 3.333333f;
        float rollScale = 2.8f;
        float yawScale = 0.0f;
        float forwardMultiplier = 1f;
        float sideMultiplier = 1f;

        float successITI = 5.0f;
        float failureITI = 15.0f;

        // Teensy for single reward
        public SerialPort serial_temp;


        [MenuItem("Window/MouseVR")]
        public static void ShowWindow()
        {
            if (window == null)
            {
                window = GetWindow<TaskManager>("Task manager", true);
            }
        }

        private void OnEnable()
        {
            LoadLists();
            LoadCalibration();
        }

        // Read the animal/task dropdown lists once (writing defaults if missing).
        // Done here rather than in OnGUI() so we don't touch the disk every repaint.
        private void LoadLists()
        {
            if (!File.Exists(animalListFile))
            {
                using (StreamWriter file = new StreamWriter(animalListFile))
                    file.Write("test,ANM001,ANM002");
            }
            using (StreamReader reader = File.OpenText(animalListFile))
                animalList = reader.ReadLine().Split(',');

            if (!File.Exists(taskListFile))
            {
                using (StreamWriter file = new StreamWriter(taskListFile))
                    file.Write("Beacon,Zigzag_B_easy,Zigzag_B,Alternation,Zigzag_A,Zigzag_A_easy,Zigzag_A_superEasy,Linear_A");
            }
            using (StreamReader reader = File.OpenText(taskListFile))
                taskList = reader.ReadLine().Split(',');
        }

        // Load reward sizes from calibrate.py's JSON. Each entry's uL drives the
        // iReward/rewardMax cap; its duration (ms) is sent to the Teensy valve.
        private void LoadCalibration()
        {
            rewardCalib = new CalibTarget[0];
            if (File.Exists(calibFile))
            {
                try
                {
                    CalibFile cf = JsonUtility.FromJson<CalibFile>(File.ReadAllText(calibFile));
                    if (cf != null && cf.targets != null)
                        rewardCalib = cf.targets;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Calibration load failed: " + e.Message);
                }
            }
            rewardLabels = new string[rewardCalib.Length];
            for (int i = 0; i < rewardCalib.Length; i++)
                rewardLabels[i] = $"{rewardCalib[i].target_ul:0.#} uL  ({rewardCalib[i].duration_ms:0} ms)";
        }

        // Launch the Python calibration GUI (Asset/teensy/calibrate.py) with the
        // conda 'base' interpreter (miniconda3). It writes water_calibration.json;
        // press Reload afterwards to pick up the new values.
        private void OpenCalibration()
        {
            string scriptPath = null;
            foreach (string guid in AssetDatabase.FindAssets("calibrate"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith("calibrate.py"))
                {
                    scriptPath = Path.GetFullPath(p);
                    break;
                }
            }
            if (scriptPath == null || !File.Exists(scriptPath))
            {
                Debug.LogError("calibrate.py not found in the project.");
                return;
            }

            string python = CondaBasePython();
            if (python == null)
            {
                Debug.LogError("conda base Python not found. Set CONDA_PYTHON_EXE or install miniconda3/anaconda3 in the default location.");
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = python,
                    Arguments = "\"" + scriptPath + "\"",
                    WorkingDirectory = Path.GetDirectoryName(scriptPath),
                    UseShellExecute = false,
                });
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to launch calibrate.py: " + e.Message);
            }
        }

        // Locate the conda 'base' interpreter. Prefer CONDA_PYTHON_EXE (set when the
        // editor is launched from a conda-initialised shell), else probe the default
        // miniconda3/anaconda3 install dirs for the running platform.
        private string CondaBasePython()
        {
            string env = Environment.GetEnvironmentVariable("CONDA_PYTHON_EXE");
            if (!string.IsNullOrEmpty(env) && File.Exists(env))
                return env;

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidates;
            if (Application.platform == RuntimePlatform.WindowsEditor)
                candidates = new[] {
                    Path.Combine(home, "miniconda3", "python.exe"),
                    Path.Combine(home, "anaconda3", "python.exe"),
                };
            else
                candidates = new[] {
                    Path.Combine(home, "miniconda3", "bin", "python"),
                    Path.Combine(home, "anaconda3", "bin", "python"),
                };
            foreach (string c in candidates)
                if (File.Exists(c))
                    return c;
            return null;
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

            // Lists are loaded once in OnEnable; guard + clamp in case the window
            // opened before OnEnable ran or an index fell out of range.
            if (animalList == null || taskList == null)
                LoadLists();
            if (animalList.Length > 0)
                animalIndex = Mathf.Clamp(animalIndex, 0, animalList.Length - 1);
            if (taskList.Length > 0)
                taskIndex = Mathf.Clamp(taskIndex, 0, taskList.Length - 1);

            GUILayout.Label("Task parameters", EditorStyles.boldLabel);
            animalIndex = EditorGUILayout.Popup("Animal name", animalIndex, animalList);
            taskIndex = EditorGUILayout.Popup("Task type", taskIndex, taskList);
            nTrial = EditorGUILayout.IntField("Total trial number", nTrial);
            EditorGUILayout.Space(10);
            GUILayout.Label("Cue parameters", EditorStyles.boldLabel);
            successITI = EditorGUILayout.FloatField("successITI (s)", successITI);
            failureITI = EditorGUILayout.FloatField("failureITI (s)", failureITI);
            // delayDurationStart = EditorGUILayout.FloatField("Cue distance min (cm)", delayDurationStart);
            // delayDurationMean = EditorGUILayout.FloatField("Cue distance mean (cm)", delayDurationMean);
            // delayDurationEnd = EditorGUILayout.FloatField("Cue distance max (cm)", delayDurationEnd);
            EditorGUILayout.Space(10);
            GUILayout.Label("Reward parameters", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (rewardCalib.Length > 0)
            {
                rewardIndex = Mathf.Clamp(rewardIndex, 0, rewardCalib.Length - 1);
                rewardIndex = EditorGUILayout.Popup("Reward (calibrated)", rewardIndex, rewardLabels);
            }
            else
            {
                EditorGUILayout.LabelField("Reward", "no calibration - run calibrate.py");
            }
            if (GUILayout.Button("Calibrate", GUILayout.MaxWidth(75)))
                OpenCalibration();
            if (GUILayout.Button("Reload", GUILayout.MaxWidth(70)))
                LoadCalibration();
            EditorGUILayout.EndHorizontal();
            rewardMax = EditorGUILayout.IntField("Maximum Reward (uL)", rewardMax);
            // punishmentLatency = EditorGUILayout.FloatField("Air puff latency (s)", punishmentLatency);
            // punishmentDuration = EditorGUILayout.FloatField("Air puff duration (s)", punishmentDuration);

            EditorGUILayout.Space(10);
            GUILayout.Label("Display", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            lightIntensity = EditorGUILayout.Slider("Light intensity", lightIntensity, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
                RenderSettings.ambientLight = Color.white * lightIntensity;

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Notes", GUILayout.MaxWidth(80));
            notes = EditorGUILayout.TextArea(notes, GUILayout.Height(40));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(20);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Ready"))
                Ready();
            if (GUILayout.Button("Start"))
                Start();
            if (GUILayout.Button("Stop"))
                Stop();
            EditorGUILayout.EndHorizontal();

            showConfig = EditorGUILayout.Foldout(showConfig, "Config");
            if (showConfig) {
                GUILayout.Label("Connections", EditorStyles.boldLabel);
                enableKeyboard = EditorGUILayout.Toggle("Enable Keyboard", enableKeyboard);
                comPortPixArt = EditorGUILayout.TextField("COM Port PixArt", comPortPixArt);
                comPortTeensy = EditorGUILayout.TextField("COM Port Teensy", comPortTeensy);

                EditorGUILayout.Space(10);
                GUILayout.Label("Ball parameters", EditorStyles.boldLabel);
                reverseDirection = EditorGUILayout.Toggle("Reverse direction", reverseDirection);
                allowRotationRoll = EditorGUILayout.Toggle("Allow rotation by roll", allowRotationRoll);
                allowRotationYaw = EditorGUILayout.Toggle("Allow rotation by yaw", allowRotationYaw);
                maxRotationSpeed = EditorGUILayout.FloatField("Max rotation speed (degree/s)", maxRotationSpeed);
                // followPath = EditorGUILayout.Toggle("Follow path", followPath);
                // pathRotationMix = EditorGUILayout.FloatField("Ratio btw auto and manual rotation", pathRotationMix);
                logTreadmill = EditorGUILayout.Toggle("Log treadmill", logTreadmill);
                pitchScale = EditorGUILayout.FloatField("Pitch scale (degree/pixel)", pitchScale);
                rollScale = EditorGUILayout.FloatField("Roll scale (degree/pixel)", rollScale);
                yawScale = EditorGUILayout.FloatField("Yaw scale (degree/pixel)", yawScale);
                forwardMultiplier = EditorGUILayout.FloatField("Forward multiplier", forwardMultiplier);
                sideMultiplier = EditorGUILayout.FloatField("Side multiplier", sideMultiplier);
                // if (GUILayout.Button("Reward"))
                //     Water();   //here!1 
                EditorGUILayout.Space(20);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("water"))
                    Water();
                if (GUILayout.Button("restart"))
                    Restart();
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndVertical();
        }

        private void OnDestroy() {
        }

        // Copy the GUI parameters onto the TaskController. Shared by Setup() and Ready()
        // so the two can't drift (Setup() used to omit rewardMax and comPort).
        private void ApplyTaskParameters(TaskController tc)
        {
            tc.animalName = animalList[animalIndex];
            tc.task = taskList[taskIndex];
            tc.nTrial = nTrial;
            tc.successITI = successITI;
            tc.failureITI = failureITI;
            tc.delayDurationStart = delayDurationStart;
            tc.delayDurationMean = delayDurationMean;
            tc.delayDurationEnd = delayDurationEnd;
            if (rewardCalib.Length > 0)
            {
                int i = Mathf.Clamp(rewardIndex, 0, rewardCalib.Length - 1);
                tc.rewardAmount = Mathf.RoundToInt(rewardCalib[i].target_ul);
                tc.rewardDuration = Mathf.RoundToInt(rewardCalib[i].duration_ms);
            }
            tc.rewardMax = rewardMax;
            tc.punishmentLatency = punishmentLatency;
            tc.punishmentDuration = punishmentDuration;
            tc.note = notes;
            tc.comPort = comPortTeensy;
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

            ApplyTaskParameters(taskController);

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

            // Lighting: remove the legacy point light (the GameObject named
            // "Directional Light", which actually held a point light) and light every
            // object evenly with flat ambient light instead.
            GameObject oldLight = GameObject.Find("Directional Light");
            if (oldLight != null)
                DestroyImmediate(oldLight);

            RenderSettings.skybox = new Material(Shader.Find("Standard"));
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;  // Lighting window: Environment Lighting Source = "Color"
            RenderSettings.ambientLight = Color.white * lightIntensity;          // even illumination, brightness from the GUI slider

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
            if (player == null)
            {
                Debug.LogError("Run Setup before Ready: no 'Player' in the scene.");
                return;
            }

            taskController = player.GetComponent<TaskController>();
            ApplyTaskParameters(taskController);

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
            playerController.comPortPixArt = comPortPixArt;

            // Start application
            UnityEditor.EditorApplication.isPlaying = true;

            Vr.BlankDisplay(true);
            Vr.Connect(false);
        }

        private void Start()
        {
            player = GameObject.Find("Player");
            taskController = player == null ? null : player.GetComponent<TaskController>();
            if (taskController == null)
            {
                Debug.LogError("Run Setup/Ready before Start.");
                return;
            }
            Vr.BlankDisplay(false);
            Vr.Connect(true);

            taskController.iState = TaskController.States.Start;
        }

        private void Stop()
        {
            if (taskController != null)
                taskController.Quit();
        }
        private void Water()
        {
            if (taskController != null)
                taskController.Reward();
        }
        private void Restart()
        {
            if (taskController != null)
                taskController.Restart();
        }

        // private void Water()
        // {
        //     serial_temp = new SerialPort(comPortPixArt, 115200);
        //     try
        //     {
        //         serial_temp.Open();
        //         if (serial_temp.IsOpen)
        //         {
        //             serial_temp.Write("w");
        //             Debug.Log("Reward");
        //         }
        //     }
        //     catch
        //     {
        //         Debug.Log(serial_temp + " is not available");
        //     }
        //     // if (_isOpen)
        //     // {
        //     //     // Send message to Teensy to give the reward
        //     //     serial.Write("w");
        //     //     iReward += rewardAmount;
        //     //     Debug.Log("Reward");
        //     // }
        //     // taskController.Reward();
        // }


        GameObject player, environment, mainCamera;

        private static TaskManager window;
        TaskController taskController;
        PlayerController playerController;
        ForceRenderRate forceRenderRate;

        // Mirrors the JSON written by Asset/teensy/calibrate.py (water_calibration.json).
        [Serializable] public class CalibTarget { public float target_ul; public float duration_ms; public int n_pulses; }
        [Serializable] public class CalibFile { public float gap_ms; public CalibTarget[] targets; }
    }
}
