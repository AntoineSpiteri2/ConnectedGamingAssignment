using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using Unity.Netcode.Transports.UTP;
using System;
public class NetworkUI : MonoBehaviour
{
    [SerializeField] private Button ServerButton;
    [SerializeField] private Button ClientButton;
    [SerializeField] private Button HostButton;
    [SerializeField] private GameObject Panel;
    private UnityTransport transport;
    private void Awake()
    {
        ClientButton.onClick.AddListener(StartClient);
        HostButton.onClick.AddListener(StartHost);
    }


   void StartClient()
    {
        try
        {
            if (!NetworkManager.Singleton.StartClient())
            {
                Debug.LogError("Failed to start client.");
                return;
            }
            Destroy(Panel);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception occurred while starting client: {ex.Message}");
        }
    }

    private void StartHost()
    {
        try
        {
            if (!NetworkManager.Singleton.StartHost())
            {
                Debug.LogError("Failed to start host.");
                return;
            }
            Debug.Log($"Server started listening on {transport.ConnectionData.ServerListenAddress} and port {transport.ConnectionData.Port}");
            CheckIfRunningLocally();
            Destroy(Panel);

        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception occurred while starting host: {ex.Message}");
        }
    }

    private void CheckIfRunningLocally()
    {
        if (transport.ConnectionData.ServerListenAddress == "127.0.0.1")
        {
            Debug.LogWarning("Server is listening locally (127.0.0.1) ONLY!");
        }
    }

    private void Start()
    {
        transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        
        if (Application.platform == RuntimePlatform.WindowsPlayer)
        {
            Debug.Log("Game is running in build mode");
        }else if (Application.isEditor)
        {
            Debug.Log("Game is running in Unity Editor");
        }else if (Application.platform == RuntimePlatform.LinuxServer && Application.isBatchMode &&
                  !Application.isEditor)
        {
            Debug.Log("Game is running on Linux Dedicated Server");
        }

        if (NetworkManager.Singleton != null)
        {
            Debug.Log($"UTP working with IP:{transport.ConnectionData.Address} and Port:{transport.ConnectionData.Port}");
        }
    }
    
    
}
