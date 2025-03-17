using Firebase.Firestore;
using Firebase.Extensions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using Unity.Netcode;

public class DLCManager : MonoBehaviour
{
    public GameObject[] storeItemPrefab;  // Prefabs for store items (Image, Buy, and Use buttons)
    private FirebaseFirestore db;
    private Dictionary<string, GameObject> storeItems = new Dictionary<string, GameObject>();

    public RawImage BlackImage;
    public RawImage WhiteImage;
    public Button StoreBtn;
    public TMP_Text CoinsText;

    private int userCoins = 0;
    public string userID = "0"; // Change to the correct logged-in user ID

    public static DLCManager Instance { get; private set; }
    private void Awake()
    {

        if (Instance == null) Instance = this;

    }
    void Start()
    {
        db = FirebaseFirestore.DefaultInstance;

        LoadUserCoins();
        LoadPFPs();
    }

    /// <summary>
    /// Loads the user's coins from Firestore.
    /// </summary>
    private void LoadUserCoins()
    {
        db.Collection("Users").Document(userID).GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCompleted && task.Result.Exists)
            {
                userCoins = task.Result.GetValue<int>("Coins");
                CoinsText.text = "Coins: " + userCoins;
            }
        });
    }

    /// <summary>
    /// Loads all available profile pictures from Firestore and sets them in the store.
    /// </summary>
    public void LoadPFPs()
    {
        db.Collection("PFPs").GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCompleted)
            {
                int index = 0;

                foreach (DocumentSnapshot doc in task.Result.Documents)
                {
                    if (index >= storeItemPrefab.Length) break;

                    string id = doc.Id;
                    string imageURL = doc.GetValue<string>("imageURL");
                    bool blackOwned = doc.GetValue<bool>("BlackOwned");
                    bool whiteOwned = doc.GetValue<bool>("WhiteOwned");

                    GameObject item = storeItemPrefab[index];
                    storeItems[id] = item;

                    GameObject ImageContainer = item.transform.GetChild(0).gameObject;
                    RawImage imageComponent = ImageContainer.GetComponent<RawImage>();
                    Button buyButton = ImageContainer.transform.GetChild(0).gameObject.GetComponent<Button>();
                    Button useButton = ImageContainer.transform.GetChild(1).gameObject.GetComponent<Button>();

                    StartCoroutine(LoadImageFromURL(imageURL, imageComponent));

                    // If already owned, disable Buy button and enable Use button
                    if (blackOwned || whiteOwned)
                    {
                        buyButton.interactable = false;
                        useButton.interactable = true;
                        useButton.onClick.AddListener(() => ApplyPFP(id, imageURL));
                    }
                    else
                    {
                        buyButton.onClick.AddListener(() => PurchasePFP(id, buyButton, useButton, imageURL));
                        useButton.interactable = false;
                    }

                    index++;
                }
            }
        });
    }

    /// <summary>
    /// Downloads and sets the image from Firebase Storage.
    /// </summary>
    private IEnumerator LoadImageFromURL(string url, RawImage targetImage)
    {
        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(www.error);
            }
            else
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(www);
                targetImage.texture = texture;
            }
        }
    }

    /// <summary>
    /// Handles the purchase of a profile picture, deducting coins and updating Firestore.
    /// </summary>
    public void PurchasePFP(string pfpID, Button buyButton, Button useButton, string imageURL)
    {
        if (userCoins < 10)
        {
            Debug.Log("Not enough coins to buy this PFP!");
            return;
        }

        userCoins -= 10;
        CoinsText.text = "Coins: " + userCoins;

        // Update coins in Firestore
        db.Collection("Users").Document(userID).UpdateAsync(new Dictionary<string, object>
        {
            { "Coins", userCoins }
        });

        // Mark as purchased in Firestore
        db.Collection("PFPs").Document(pfpID).UpdateAsync(new Dictionary<string, object>
        {
            { "BlackOwned", true },
            { "WhiteOwned", true }
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

    /// <summary>
    /// Applies the selected profile picture and syncs it across clients.
    /// </summary>
    public void ApplyPFP(string pfpID, string imageURL)
    {
        // Save selected PFP in Firestore
        db.Collection("Users").Document(userID).UpdateAsync(new Dictionary<string, object>
        {
            { "ImageUrl", imageURL }
        });

        // Load the image for the respective player
        StartCoroutine(LoadPFPForPlayer(imageURL));

        // Sync across all clients
        GameManager.Instance.SetPlayerPFPServerRpc(NetworkManager.Singleton.LocalClientId, pfpID);
    }

    /// <summary>
    /// Loads and applies the profile picture for the player.
    /// </summary>
    private IEnumerator LoadPFPForPlayer(string url)
    {
        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(www.error);
            }
            else
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(www);
                if (GameManager.Instance.connectedPlayers.Count > 0 && GameManager.Instance.connectedPlayers[0] == NetworkManager.Singleton.LocalClientId)
                {
                    WhiteImage.texture = texture; // Apply to White player
                }
                else
                {
                    BlackImage.texture = texture; // Apply to Black player
                }
            }
        }
    }

    /// <summary>
    /// Loads the previously selected PFP for the user when they connect.
    /// Call this in OnClientConnected.
    /// </summary>
    public void LoadUserSelectedPFP()
    {
        db.Collection("Users").Document(userID).GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCompleted && task.Result.Exists)
            {
                if (task.Result.ContainsField("ImageUrl"))
                {
                    string imageURL = task.Result.GetValue<string>("ImageUrl");

                    if (!string.IsNullOrEmpty(imageURL))
                    {
                        Debug.Log($"Loading stored PFP: {imageURL}");
                        StartCoroutine(LoadPFPForPlayer(imageURL));
                    }
                }
            }
        });
    }

}
