using UnityEngine;                             // Provides core Unity engine classes.
using Unity.Netcode;                           // Supports Unity's networking functionality.
using UnityEngine.UI;                          // Provides UI elements like Button.
using Unity.Netcode.Transports.UTP;              // Provides Unity Transport Protocol (UTP) for network communication.
using System;                                  // Provides base .NET functionality.
using System.Text.RegularExpressions;          // For matching strings against regular expressions.
using TMPro;                                   // Provides TextMesh Pro UI elements.
using System.Net.Sockets;                      // For handling low-level socket operations.
using System.Net;                              // Provides access to network information like DNS.

/// <summary>
/// Manages the UI for network operations (client, host) and IP addressing.
/// Inherits from MonoBehaviourSingleton to ensure there's only one instance throughout the game.
/// </summary>
public class NetworkUI : MonoBehaviourSingleton<NetworkUI>
{
    // UI Button to start as server (not hooked up in this snippet but may be used in the future).
    [SerializeField] private Button ServerButton;
    // UI Button to start as client.
    [SerializeField] private Button ClientButton;
    // UI Button to start as host.
    [SerializeField] private Button HostButton;
    // Panel that contains the network UI elements (e.g., input fields, buttons).
    [SerializeField] public GameObject Panel;
    // Input field for entering the IP address (TextMesh Pro version).
    [SerializeField] private TMP_InputField IpText;

    // Reference to the UnityTransport component for managing network connection parameters.
    private UnityTransport transport;

    /// <summary>
    /// Awake is called when the script instance is being loaded.
    /// Here, we register UI button click events.
    /// </summary>
    private void Awake()
    {
        // Register the StartClient method to be called when the client button is clicked.
        ClientButton.onClick.AddListener(StartClient);
        // Register the StartHost method to be called when the host button is clicked.
        HostButton.onClick.AddListener(StartHost);
    }

    /// <summary>
    /// Retrieves the first IPv4 address found on the local machine.
    /// </summary>
    /// <returns>Local IPv4 address as a string.</returns>
    private string GetLocalIPAddress()
    {
        // Get host entry information for the local machine.
        var host = Dns.GetHostEntry(Dns.GetHostName());
        // Iterate over each IP address in the address list.
        foreach (var ip in host.AddressList)
        {
            // Check if the IP address is an IPv4 address.
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        // Throw an exception if no IPv4 address is found.
        throw new Exception("No network adapters with an IPv4 address found.");
    }

    /// <summary>
    /// Start is called before the first frame update.
    /// Here, the network transport is initialized and diagnostic messages are logged.
    /// </summary>
    private void Start()
    {
        // Retrieve the UnityTransport component from the NetworkManager singleton.
        transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        // Log environment-related debug messages based on the runtime platform.
        if (Application.platform == RuntimePlatform.WindowsPlayer)
        {
            if (GameManager.Instance.DebugMode) Debug.Log("Game is running in build mode");
        }
        else if (Application.isEditor)
        {
            if (GameManager.Instance.DebugMode) Debug.Log("Game is running in Unity Editor");
        }
        else if (Application.platform == RuntimePlatform.LinuxServer && Application.isBatchMode && !Application.isEditor)
        {
            if (GameManager.Instance.DebugMode) Debug.Log("Game is running on Linux Dedicated Server");
        }

        // Log network transport settings if the NetworkManager singleton is available.
        if (NetworkManager.Singleton != null)
        {
            if (GameManager.Instance.DebugMode)
                Debug.Log($"UTP working with IP:{transport.ConnectionData.Address} and Port:{transport.ConnectionData.Port}");
        }
    }

    /// <summary>
    /// Starts the client using the IP address entered by the user.
    /// Validates the IP and then attempts to start the client.
    /// </summary>
    private void StartClient()
    {
        try
        {
            // Retrieve and trim the IP address entered by the user.
            string ip = IpText.text.Trim();

            // Validate the IP address format using a regular expression.
            if (!IsValidIPAddress(ip))
            {
                // If invalid, set the input field text to an error message and exit.
                IpText.text = "Invalid IP Address!";
                return;
            }

            // Set the transport's connection address to the user-provided IP.
            transport.ConnectionData.Address = ip;

            // Attempt to start the client using the NetworkManager.
            if (!NetworkManager.Singleton.StartClient())
            {
                // Log an error message if the client fails to start.
                if (GameManager.Instance.DebugMode) Debug.LogError("Failed to start client.");
                return;
            }
            // Hide the network UI panel once the client starts successfully.
            Panel.SetActive(false);
        }
        catch (Exception ex)
        {
            // Log any exceptions encountered during client startup.
            if (GameManager.Instance.DebugMode)
                Debug.LogError($"Exception occurred while starting client: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts the host which acts as both server and client.
    /// Also retrieves the local IP address for clients to connect.
    /// </summary>
    private void StartHost()
    {
        try
        {
            // Retrieve the local IPv4 address to advertise for client connection.
            string localIP = GetLocalIPAddress();
            Debug.Log("Host LAN IP is: " + localIP);

            // Bind to all available network interfaces by setting the address to "0.0.0.0".
            transport.ConnectionData.Address = "0.0.0.0";

            // Attempt to start the host using the NetworkManager.
            if (!NetworkManager.Singleton.StartHost())
            {
                // Log an error if the host fails to start.
                if (GameManager.Instance.DebugMode) Debug.LogError("Failed to start host.");
                return;
            }

            // Log a success message along with the port information to be shared with clients.
            Debug.Log($"Server started. Advertise this IP to clients: {localIP} and port {transport.ConnectionData.Port}");
            // Hide the network UI panel since the network has been started.
            Panel.SetActive(false);
        }
        catch (Exception ex)
        {
            // Log any exceptions encountered during host startup.
            if (GameManager.Instance.DebugMode)
                Debug.LogError($"Exception occurred while starting host: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates if the given string is a correctly formatted IPv4 address.
    /// </summary>
    /// <param name="ip">The IP address string to validate.</param>
    /// <returns>True if valid; otherwise, false.</returns>
    private bool IsValidIPAddress(string ip)
    {
        // Regular expression pattern to match valid IPv4 addresses.
        string pattern = @"^(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)\." +
                         @"(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)\." +
                         @"(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)\." +
                         @"(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)$";
        // Use Regex.IsMatch to verify that the IP address matches the pattern.
        return Regex.IsMatch(ip, pattern);
    }
}
