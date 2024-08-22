using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

// Implement all VR related functions here (teleportation, reward, sound effect)

namespace Janelia
{
    public class Vr
    {
        public Dictionary<string, Vector3> starts = new Dictionary<string, Vector3>();
        public Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
        public PlayerController playerController;
        public GameObject player, env;

        public bool _isLightOn, _isConnected;

        // Get the list of objects that needs to be controlled during task.
        public void Start()
        {
            player = GameObject.Find("Player");
            if (player == null) Debug.LogError("The object 'Player' should exist.");
            playerController = player.GetComponent<PlayerController>();
            _isConnected = playerController.allowMovement;

            env = GameObject.Find("Environment");
            if (env != null)
            {
                MeshFilter[] meshs = env.GetComponentsInChildren<MeshFilter>();

                // Teleport target and potential movable objects
                foreach (MeshFilter mesh in meshs)
                {
                    string name = mesh.transform.name.ToLower(); // Let's use lower case naming only.
                    models.Add(name, mesh.gameObject);

                    string[] subname = name.Trim('_').Split('_');
                    if (subname[0].ToLower().Contains("start"))
                    {
                        if (subname.Length == 2)
                        {
                            starts.Add(subname[1], mesh.transform.position);
                        }
                        else // no name
                        {
                            starts.Add("", mesh.transform.position);
                        }
                    }
                }
            }
        }

        public static void BlankDisplay(bool state) // true: off, false: on
        {
            Light[] lights = GameObject.FindObjectsOfType<Light>();
            foreach (Light light in lights)
                light.enabled = !state;
        }

        public static void BlankDisplay()
        {
            Light[] lights = GameObject.FindObjectsOfType<Light>();
            foreach (Light light in lights)
                light.enabled = !light.enabled;
        }

        public static void Connect(bool state)
        {
            PlayerController playerController = GameObject.FindObjectOfType<PlayerController>();
            playerController.allowMovement = state;
        }

        public static void Connect()
        {
            PlayerController playerController = GameObject.FindObjectOfType<PlayerController>();
            playerController.allowMovement = !playerController.allowMovement;
        }

        public static void Quit()
        {
            #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
            #elif UNITY_STANDALONE
                Application.Quit();
            #endif
        }

        public void Teleport(Vector3 position, Vector3 rotation)
        {
            if (player == null)
                player = GameObject.Find("Player");
            player.transform.position = position;
            player.transform.rotation = Quaternion.Euler(rotation);
        }

        public void Teleport(Vector3 position, float rotation)
        {
            if (player == null)
                player = GameObject.Find("Player");
            player.transform.position = position;
            player.transform.rotation = Quaternion.Euler(0, rotation, 0);
        }

        public void Teleport(Vector3 position)
        {
            if (player == null)
                player = GameObject.Find("Player");
            player.transform.position = position;
        }

        public void Teleport(string position)
        {
            if (player == null)
                Start();
            if (starts.ContainsKey(position))
            {
                player.transform.position = starts[position];
            }
            else
            {
                Debug.Log("No teleport target exists");
            }
        }

        public void Teleport(float pos_x, float pos_z)
        {
            Teleport(new Vector3(pos_x, 0f, pos_z));
        }

        public void ApplyPhysics(string name, bool state=true)
        {
            if (models == null)
                Start();
            if (models.ContainsKey(name))
            {
                MeshCollider meshCollider = models[name].GetComponent<MeshCollider>();
                if (meshCollider == null && state)
                    meshCollider = models[name].AddComponent<MeshCollider>();
                else
                    meshCollider.enabled = state;

                Rigidbody rigidbody = models[name].GetComponent<Rigidbody>();
                if (rigidbody == null && state)
                    rigidbody = models[name].AddComponent<Rigidbody>();
                else if (rigidbody != null && !state)
                    GameObject.Destroy(rigidbody);
            }
        }

        public void Move(string name, Vector3 position)
        {
            if (models == null)
                Start();
            if (models.ContainsKey(name))
            {
                models[name].transform.position = position;
            }
        }

        public Vector3 GetPosition(string name)
        {
            if (models == null)
                Start();
            if (name.StartsWith("console"))
                return player.transform.position;
            else if (models.ContainsKey(name))
                return models[name].transform.position;
            else
                return Vector3.zero;
        }

        public Vector3 GetPosition()
        {
            return player.transform.position;
        }
    }
}
