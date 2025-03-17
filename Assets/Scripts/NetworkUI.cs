using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using Unity.Netcode.Transports.UTP;
using System;
using System.Text.RegularExpressions;
using TMPro;

public class NetworkUI : MonoBehaviour
{
    [SerializeField] private Button ServerButton;
    [SerializeField] private Button ClientButton;
    [SerializeField] private Button HostButton;
    [SerializeField] private GameObject Panel;
    [SerializeField] private TMP_InputField IpText; // Changed to InputField

    private UnityTransport transport;

    private void Awake()
    {
        ClientButton.onClick.AddListener(StartClient);
        HostButton.onClick.AddListener(StartHost);
    }

    private void Start()
    {
        transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        if (Application.platform == RuntimePlatform.WindowsPlayer)
        {
            if (GameManager.Instance.DebugMode) Debug.Log("Game is running in build mode");
        }
        else if (Application.isEditor)
        {
            if (GameManager.Instance.DebugMode) Debug.Log("Game is running in Unity Editor");
        }
        else if (Application.platform == RuntimePlatform.LinuxServer && Application.isBatchMode &&
                  !Application.isEditor)
        {
            if (GameManager.Instance.DebugMode) Debug.Log("Game is running on Linux Dedicated Server");
        }

        if (NetworkManager.Singleton != null)
        {
            if (GameManager.Instance.DebugMode) Debug.Log($"UTP working with IP:{transport.ConnectionData.Address} and Port:{transport.ConnectionData.Port}");
        }
    }

    private void StartClient()
    {
        try
        {
            string ip = IpText.text.Trim();

            if (!IsValidIPAddress(ip))
            {
                IpText.text = "Invalid IP Address!";
                return;
            }

            transport.ConnectionData.Address = ip; // Set custom IP before starting client

            if (!NetworkManager.Singleton.StartClient())
            {
                if (GameManager.Instance.DebugMode) Debug.LogError("Failed to start client.");
                return;
            }
            Destroy(Panel);
        }
        catch (Exception ex)
        {
            if (GameManager.Instance.DebugMode) Debug.LogError($"Exception occurred while starting client: {ex.Message}");
        }
    }

    private void StartHost()
    {
        try
        {
            transport.ConnectionData.Address = "0.0.0.0"; // Listen on all available network interfaces

            if (!NetworkManager.Singleton.StartHost())
            {
                if (GameManager.Instance.DebugMode) Debug.LogError("Failed to start host.");
                return;
            }

            Debug.Log($"Server started listening on {transport.ConnectionData.Address} and port {transport.ConnectionData.Port}");
            CheckIfRunningLocally();
            Destroy(Panel);
        }
        catch (Exception ex)
        {
            if (GameManager.Instance.DebugMode) Debug.LogError($"Exception occurred while starting host: {ex.Message}");
        }
    }

    private void CheckIfRunningLocally()
    {
        if (transport.ConnectionData.Address == "127.0.0.1")
        {
            if (GameManager.Instance.DebugMode) Debug.LogWarning("Server is listening locally (127.0.0.1) ONLY!");
        }
    }

    private bool IsValidIPAddress(string ip)
    {
        string pattern = @"^(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)$";
        return Regex.IsMatch(ip, pattern);
    }
}
