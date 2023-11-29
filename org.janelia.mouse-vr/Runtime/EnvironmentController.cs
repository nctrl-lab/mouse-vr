using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// This script tries to follow naming convention in MouseoVeR
public class EnvironmentController : MonoBehaviour
{

    void Awake()
    {
        MeshRenderer[] meshs = GetComponentsInChildren<MeshRenderer>();

        foreach (MeshRenderer mesh in meshs)
        {
            MeshCollider meshcollider = mesh.GetComponent<MeshCollider>();

            // _name_: invisible
            if (mesh.name.StartsWith("_"))
            {
                mesh.enabled = false;
            }
            else
            {
                // I will just enable collider for all subject for all visible objects
                if (meshcollider == null)
                {
                    meshcollider = mesh.gameObject.AddComponent<MeshCollider>();
                }
                else
                {
                    meshcollider.enabled = true;
                }
            }

            string name = mesh.name.Trim('_');
            string[] subname = name.Split('_');

            if (subname.Length >= 2)
            {
                // name_p: physics enabled
                if (subname[subname.Length - 1].Contains('p'))
                {
                    if (meshcollider == null)
                    {
                        meshcollider = mesh.gameObject.AddComponent<MeshCollider>();
                    }
                    else
                    {
                        meshcollider.enabled = true;
                    }

                    // name_pm: movable
                    if (subname[subname.Length - 1].Contains('m'))
                    {
                        Rigidbody rigidbody = mesh.GetComponent<Rigidbody>();
                        if (rigidbody == null)
                        {
                            rigidbody = mesh.gameObject.AddComponent<Rigidbody>();
                        }
                    }

                    // name_pr: reportable / penatrable
                    if (subname[subname.Length - 1].Contains('r') && !subname[subname.Length - 1].Contains('i'))
                    {
                        meshcollider.convex = true;
                        meshcollider.isTrigger = true;
                    }
                }
            }
        }
    }
}