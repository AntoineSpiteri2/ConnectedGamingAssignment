using UnityEngine;
using System;

[Serializable]
public class TransformData
{
    public string name;
    public Vector3 position;
    public Quaternion rotation;

    public TransformData() { }

    public TransformData(Transform t)
    {
        name = t.name;
        position = t.position;
        rotation = t.rotation;
    }
}
