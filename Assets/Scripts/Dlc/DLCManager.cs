using Firebase.Firestore;                             // Provides access to Firestore database functionality.
using Firebase.Extensions;                            // Provides extension methods (for example, ContinueWithOnMainThread).
using UnityEngine;                                    // Core Unity engine classes.
using UnityEngine.UI;                                 // Provides UI elements like RawImage, Button.
using TMPro;                                          // Provides TextMeshPro UI elements.
using System.Collections;                             // Provides the non-generic IEnumerator used for coroutines.
using System.Collections.Generic;                     // Provides generic collections, e.g., Dictionary and List.
using UnityEngine.Networking;                         // Provides UnityWebRequest for downloading data.
using Unity.Netcode;                                  // Provides Unity Netcode classes for networking.

/// <summary>
/// DLCManager handles the DLC (Downloadable Content) store functionality,
/// including loading profile pictures (PFPs), processing purchases,
/// applying selected PFPs, and syncing the data with Firestore and across clients.
/// Inherits from NetworkBehaviour to integrate with Unity Netcode.
/// </summary>
public class DLCManager : NetworkBehaviour
{
    // Array of prefabs for store items (each typically contains an image along with Buy and Use buttons).
    public GameObject[] storeItemPrefab;
    // Firestore database reference.
    private FirebaseFirestore db;
    // Dictionary mapping a PFP ID (string) to its corresponding store item GameObject.
    private Dictionary<string, GameObject> storeItems = new Dictionary<string, GameObject>();

    // UI elements for displaying the player’s profile pictures.
    public RawImage BlackImage;    // Profile picture for the Black side.
    public RawImage WhiteImage;    // Profile picture for the White side.
    public Button StoreBtn;        // The button used to toggle the store visibility.
    public TMP_Text CoinsText;     // Text element to display the user's coins.
    public GameObject store;       // The DLC store UI panel.
    // The user's current coin count.
    private int userCoins = 0;
    // The user's ID (should ideally be set based on the authenticated user).
    public string userID = "0";
    // UI input field for game string; usage depends on your integration.
    public InputField GameStringInputField;

    // Singleton instance for global access.
    public static DLCManager Instance { get; private set; }

    /// <summary>
    /// Awake is called when the script instance is loaded.
    /// Sets up the singleton and configures Firestore settings.
    /// </summary>
    private void Awake()
    {
        // Set singleton instance.
        if (Instance == null) Instance = this;
        // Disable local persistence for Firestore (useful for real-time updates across clients).
        FirebaseFirestore.DefaultInstance.Settings.PersistenceEnabled = false;
    }

    /// <summary>
    /// Start initializes Firestore and (optionally) retrieves the local client ID.
    /// </summary>
    void Start()
    {
        // Get the default Firestore instance.
        db = FirebaseFirestore.DefaultInstance;
        // Example: retrieve the local client ID as a string (can be used for debugging or further processing).
        string id = NetworkManager.Singleton.LocalClientId.ToString();
    }

    /// <summary>
    /// Loads the user's coin count from Firestore.
    /// </summary>
    private void LoadUserCoins()
    {
        // Access the "Users" collection and the document corresponding to the userID.
        db.Collection("Users").Document(userID).GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            // Check if the task completed and the document exists.
            if (task.IsCompleted && task.Result.Exists)
            {
                // Retrieve the integer value from the "Coins" field.
                userCoins = task.Result.GetValue<int>("Coins");
                // Update the UI text to display the current coins.
                CoinsText.text = "Coins: " + userCoins;
            }
        });
    }

    /// <summary>
    /// Loads available profile pictures (PFPs) from Firestore and populates the store UI.
    /// </summary>
    public void LoadPFPs()
    {
        // Query the "PFPs" collection from Firestore.
        db.Collection("PFPs").GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCompleted)
            {
                int index = 0;
                // Iterate through all documents (each representing a PFP).
                foreach (DocumentSnapshot doc in task.Result.Documents)
                {
                    // Stop if we exceed the number of available prefab slots.
                    if (index >= storeItemPrefab.Length) break;

                    // Get the document ID and data fields.
                    string id = doc.Id;
                    string imageURL = doc.GetValue<string>("imageURL");
                    bool blackOwned = doc.GetValue<bool>("BlackOwned");
                    bool whiteOwned = doc.GetValue<bool>("WhiteOwned");

                    // Get the prefab instance for the store item.
                    GameObject item = storeItemPrefab[index];
                    // Store the mapping between the PFP ID and the item.
                    storeItems[id] = item;

                    // Get the container holding the image and buttons (assumes first child holds the content).
                    GameObject ImageContainer = item.transform.GetChild(0).gameObject;
                    // Retrieve UI components.
                    RawImage imageComponent = ImageContainer.GetComponent<RawImage>();
                    Button buyButton = ImageContainer.transform.GetChild(0).gameObject.GetComponent<Button>();
                    Button useButton = ImageContainer.transform.GetChild(1).gameObject.GetComponent<Button>();

                    // Start a coroutine to download and set the image from the URL.
                    StartCoroutine(LoadImageFromURL(imageURL, imageComponent));

                    // Remove any existing listeners to avoid listener stacking.
                    buyButton.onClick.RemoveAllListeners();
                    useButton.onClick.RemoveAllListeners();

                    // Reset button interactability.
                    buyButton.interactable = false;
                    useButton.interactable = false;

                    // Determine if the current user is White (userID "0") or not.
                    bool isWhite = int.Parse(userID) == 0;

                    if (isWhite)
                    {
                        if (whiteOwned)
                        {
                            // If the white PFP is already owned, enable the "Use" button.
                            useButton.interactable = true;
                            // When clicked, apply the profile picture.
                            useButton.onClick.AddListener(() => ApplyPFP(userID, imageURL));
                        }
                        else
                        {
                            // Otherwise, enable the "Buy" button.
                            buyButton.interactable = true;
                            // Set up click listener to purchase the profile picture.
                            buyButton.onClick.AddListener(() => PurchasePFP(id, buyButton, useButton, imageURL));
                        }
                    }
                    else // For Black user.
                    {
                        if (blackOwned)
                        {
                            useButton.interactable = true;
                            useButton.onClick.AddListener(() => ApplyPFP(userID, imageURL));
                        }
                        else
                        {
                            buyButton.interactable = true;
                            buyButton.onClick.AddListener(() => PurchasePFP(id, buyButton, useButton, imageURL));
                        }
                    }
                    index++;
                }
            }
        });
    }

    /// <summary>
    /// Downloads an image from a URL and applies it to a RawImage component.
    /// </summary>
    /// <param name="url">The URL of the image.</param>
    /// <param name="targetImage">The RawImage component to apply the downloaded texture.</param>
    private IEnumerator LoadImageFromURL(string url, RawImage targetImage)
    {
        // Create a UnityWebRequest to get a texture from the provided URL.
        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
        {
            // Wait for the request to complete.
            yield return www.SendWebRequest();

            // If the request failed, log the error.
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(www.error);
            }
            else
            {
                // Get the texture and assign it to the target RawImage.
                Texture2D texture = DownloadHandlerTexture.GetContent(www);
                targetImage.texture = texture;
            }
        }
    }

    /// <summary>
    /// Displays a temporary message in the CoinsText UI element.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="delay">Time (in seconds) to display the message.</param>
    private IEnumerator ShowMessage(string message, float delay)
    {
        // Set the CoinsText to the specified message.
        CoinsText.text = message;
        yield return new WaitForSeconds(delay);
        // After the delay, revert to displaying the current coin count.
        CoinsText.text = "Coins: " + userCoins;
    }

    /// <summary>
    /// Handles purchasing a profile picture (PFP), deducting coins and updating Firestore.
    /// </summary>
    /// <param name="pfpID">The ID of the profile picture to purchase.</param>
    /// <param name="buyButton">Button for buying the PFP (will be disabled post-purchase).</param>
    /// <param name="useButton">Button for applying the PFP (will be enabled post-purchase).</param>
    /// <param name="imageURL">The URL of the profile picture image.</param>
    public void PurchasePFP(string pfpID, Button buyButton, Button useButton, string imageURL)
    {
        // Check if the user has at least 10 coins; if not, show a message.
        if (userCoins < 10)
        {
            StartCoroutine(ShowMessage("Not enough coins to buy this PFP!", 2.0f));
            return;
        }

        // Deduct 10 coins for the purchase.
        userCoins = userCoins - 10;
        CoinsText.text = "Coins: " + userCoins;

        // Update the coins count in the Firestore database for the current user.
        db.Collection("Users").Document(userID).UpdateAsync(new Dictionary<string, object>
        {
            { "Coins", userCoins }
        });

        // Log the purchase event for analytics.
        AnalyticsLogger.LogDLCPurchased(userID, pfpID);

        // Check the userID to determine if the user is White ("0") or Black.
        if (int.Parse(userID) == 0)
        {
            // Update Firestore fields for a White user.
            db.Collection("PFPs").Document(pfpID).UpdateAsync(new Dictionary<string, object>
            {
                { "WhiteOwned", true },
                { "WhiteUse", true }
            }).ContinueWithOnMainThread(task =>
            {
                if (task.IsCompleted)
                {
                    Debug.Log($"PFP {pfpID} purchased!");
                    // Disable the buy button and enable the use button.
                    buyButton.interactable = false;
                    useButton.interactable = true;
                    // Set listener for applying the PFP when the use button is clicked.
                    useButton.onClick.AddListener(() => ApplyPFP(pfpID, imageURL));
                }
            });
        }
        else
        {
            // Update Firestore for a Black user.
            db.Collection("PFPs").Document(pfpID).UpdateAsync(new Dictionary<string, object>
            {
                { "BlackOwned", true },
                { "BlackUse", true }
            }).ContinueWithOnMainThread(task =>
            {
                if (task.IsCompleted)
                {
                    Debug.Log($"PFP {pfpID} purchased!");
                    buyButton.interactable = false;
                    useButton.interactable = true;
                    useButton.onClick.AddListener(() => ApplyPFP(pfpID, imageURL));
                }
            });
        }
    }

    /// <summary>
    /// Applies the selected profile picture by updating Firestore, loading the image,
    /// and syncing the change with other clients via RPCs.
    /// </summary>
    /// <param name="pfpID">The ID of the profile picture.</param>
    /// <param name="imageURL">The image URL for the profile picture.</param>
    public void ApplyPFP(string pfpID, string imageURL)
    {
        // Update the Firestore document for the current user with the selected ImageUrl.
        db.Collection("Users").Document(userID).UpdateAsync(new Dictionary<string, object>
        {
            { "ImageUrl", imageURL }
        });

        // Start a coroutine to download and apply the profile picture image.
        StartCoroutine(LoadPFPForPlayer(imageURL, userID));
        // Call a server RPC to sync the PFP image URL with all clients.
        syncPfpPictureServerRpc(imageURL, userID);

        // Also, update the profile picture reference for the player using GameManager.
        GameManager.Instance.SetPlayerPFPServerRpc(NetworkManager.Singleton.LocalClientId, pfpID);
    }

    /// <summary>
    /// Server RPC that syncs the profile picture URL across clients.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void syncPfpPictureServerRpc(string imageurl, string playerid)
    {
        // Call the client RPC to set the respective player's profile picture.
        setRespectivePLayerPfpClientRpc(imageurl, playerid);
    }

    /// <summary>
    /// Client RPC that applies the new profile picture.
    /// </summary>
    [ClientRpc]
    public void setRespectivePLayerPfpClientRpc(string imageurl, string playerid)
    {
        Debug.Log($" [Client] Applying PFP {imageurl} for player {playerid}");
        // Start coroutine to load and apply the profile picture for the specific player.
        StartCoroutine(LoadPFPForPlayer(imageurl, playerid));
    }

    /// <summary>
    /// Coroutine to download and apply a profile picture to the appropriate RawImage based on player ID.
    /// </summary>
    /// <param name="url">URL of the profile picture.</param>
    /// <param name="playerid">The ID of the player (as string).</param>
    private IEnumerator LoadPFPForPlayer(string url, string playerid)
    {
        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
        {
            // Wait for the web request to complete.
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(www.error);
            }
            else
            {
                // Get the texture from the response.
                Texture2D texture = DownloadHandlerTexture.GetContent(www);
                // Apply the texture to the appropriate RawImage.
                if (int.Parse(playerid) == 0)
                {
                    WhiteImage.texture = texture; // For White player.
                }
                else
                {
                    BlackImage.texture = texture; // For Black player.
                }
            }
        }
    }

    /// <summary>
    /// Loads the selected profile picture for all connected players based on stored Firestore data.
    /// Typically called when a client connects.
    /// </summary>
    public void LoadUserSelectedPFP()
    {
        // Iterate over each connected player.
        foreach (var player in GameManager.Instance.connectedPlayers)
        {
            // For each player, get their document from the "Users" collection.
            db.Collection("Users").Document(player.ToString()).GetSnapshotAsync().ContinueWithOnMainThread(task =>
            {
                Debug.Log($"Loading PFP for player {player}");
                if (task.IsCompleted && task.Result.Exists)
                {
                    // If the document contains an "ImageUrl" field, retrieve it.
                    if (task.Result.ContainsField("ImageUrl"))
                    {
                        string imageURL = task.Result.GetValue<string>("ImageUrl");

                        if (!string.IsNullOrEmpty(imageURL))
                        {
                            Debug.Log($"Loading stored PFP: {imageURL}");
                            // Start coroutine to load and apply the stored profile picture.
                            StartCoroutine(LoadPFPForPlayer(imageURL, player.ToString()));
                        }
                    }
                }
            });
        }
    }

    /// <summary>
    /// Toggles the DLC store visibility.
    /// Loads coins and profile pictures when opening, and updates the button text accordingly.
    /// </summary>
    public void ToggleStore()
    {
        if (store.activeSelf)
        {
            // Change button text to indicate the store is closed.
            StoreBtn.GetComponentInChildren<TMP_Text>().text = "Open DLC Store";
            // Hide the store UI.
            store.SetActive(false);
        }
        else
        {
            // Load current user coins and available profile pictures from Firestore.
            LoadUserCoins();
            LoadPFPs();
            // Update button text and show the store UI.
            StoreBtn.GetComponentInChildren<TMP_Text>().text = "Close DLC Store";
            store.SetActive(true);
        }
    }

    /// <summary>
    /// Saves the current game state to Firestore.
    /// Stores the game string in the "Game" collection, document "0".
    /// </summary>
    public void SaveGameStateToFirebase()
    {
        db.Collection("Game").Document("0").SetAsync(new Dictionary<string, object>
        {
            { "GameString", GameStringInputField.text }
        }).ContinueWithOnMainThread(task =>
        {
            if (task.IsCompleted)
            {
                Debug.Log("Game state saved to Firebase.");
            }
            else
            {
                Debug.LogError("Failed to save game state to Firebase: " + task.Exception);
            }
        });
    }

    /// <summary>
    /// Loads the saved game state from Firestore and applies it to the game.
    /// </summary>
    public void LoadGameStateFromFirebase()
    {
        db.Collection("Game").Document("0").GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCompleted && task.Result.Exists)
            {
                // Retrieve the saved game string.
                string loadedGameState = task.Result.GetValue<string>("GameString");
                Debug.Log("Loaded game state: " + loadedGameState);
                // Update the game state by loading the game string.
                GameManager.Instance.LoadGame(loadedGameState);
            }
            else
            {
                Debug.LogError("Failed to load game state from Firebase.");
            }
        });
    }
}
