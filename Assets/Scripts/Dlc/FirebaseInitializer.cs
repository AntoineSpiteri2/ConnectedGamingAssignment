using Firebase;
using Firebase.Extensions;
using Firebase.Firestore;
using Firebase.Storage;
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
                FirebaseApp app = GetFirebaseApp();
                // Instead of using DefaultInstance, obtain the instances using your custom app.
                db = FirebaseFirestore.GetInstance(app);
                storage = FirebaseStorage.GetInstance(app);
                Debug.Log("Firebase initialized successfully!");
            }
            else
            {
                Debug.LogError("Could not resolve Firebase dependencies: " + task.Result);
            }
        });
    }

    private FirebaseApp GetFirebaseApp()
    {
        // Detect if this is a ParrelSync clone. For instance, you could look for a command-line argument.
        string[] args = System.Environment.GetCommandLineArgs();
        bool isClone = false;
        foreach (string arg in args)
        {
            if (arg.Contains("clone"))
            {
                isClone = true;
                break;
            }
        }

        if (isClone)
        {
            // Create a uniquely named FirebaseApp for the clone
            return FirebaseApp.Create(FirebaseApp.DefaultInstance.Options, "CloneInstance_" + System.Guid.NewGuid());
        }
        else
        {
            // Use the default instance for the main editor instance
            return FirebaseApp.DefaultInstance;
        }
    }
}
