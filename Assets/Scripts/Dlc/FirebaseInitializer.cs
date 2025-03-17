using Firebase;
using Firebase.Extensions;
using Firebase.Firestore;
using Firebase.Storage;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FirebaseInitializer : MonoBehaviour
{
    private FirebaseFirestore db;
    private FirebaseStorage storage;

    private void Start()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                db = FirebaseFirestore.DefaultInstance;
                storage = FirebaseStorage.DefaultInstance;
                Debug.Log("Firebase initialized successfully!");
            }
            else
            {
                Debug.LogError("Could not resolve Firebase dependencies: " + task.Result);
            }
        });
    }
}
