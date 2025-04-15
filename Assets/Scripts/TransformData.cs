using UnityEngine;   // Provides Unity-specific classes such as Transform, Vector3, and Quaternion.
using System;        // Provides basic system functionality including the [Serializable] attribute.

[Serializable]       // This attribute allows instances of TransformData to be serialized (e.g., into JSON or for Unity's Inspector).
public class TransformData
{
    // Stores the name of the transform.
    public string name;
    // Stores the position of the transform as a Vector3 (x, y, z coordinates).
    public Vector3 position;
    // Stores the rotation of the transform as a Quaternion, which represents orientation.
    public Quaternion rotation;

    // Default constructor required for serialization or instantiation without parameters.
    public TransformData() { }

    // Parameterized constructor that initializes the TransformData instance from a Unity Transform.
    public TransformData(Transform t)
    {
        // Set the name field to the transform's name.
        name = t.name;
        // Set the position field to the transform's position.
        position = t.position;
        // Set the rotation field to the transform's rotation.
        rotation = t.rotation;
    }
}
