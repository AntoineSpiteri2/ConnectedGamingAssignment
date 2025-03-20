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
    public GameObject store;
    private int userCoins = 0;
    public string userID = "0"; // Change to the correct logged-in user ID
    public InputField GameStringInputField;


    public static DLCManager Instance { get; private set; }
    private void Awake()
    {

        if (Instance == null) Instance = this;
        FirebaseFirestore.DefaultInstance.Settings.PersistenceEnabled = false;


    }
    void Start()
    {
        db = FirebaseFirestore.DefaultInstance;
        string id = NetworkManager.Singleton.LocalClientId.ToString();


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
                    if (int.Parse(userID) == 0)
                    {
                        if (whiteOwned && !blackOwned)
                        {
                            buyButton.interactable = false;
                            useButton.interactable = true;
                            useButton.onClick.AddListener(() => ApplyPFP(userID, imageURL));
                        }
                        else
                        {
                            buyButton.onClick.AddListener(() => PurchasePFP(id, buyButton, useButton, imageURL));
                            buyButton.interactable = true;
                            useButton.interactable = false;
                        }
                    } else
                    {
                        if (blackOwned && !whiteOwned)
                        {
                            buyButton.interactable = false;
                            useButton.interactable = true;
                            useButton.onClick.AddListener(() => ApplyPFP(userID, imageURL));
                        }
                        else
                        {
                            buyButton.onClick.AddListener(() => PurchasePFP(id, buyButton, useButton, imageURL));
                            buyButton.interactable = true;
                            useButton.interactable = false;
                        }
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

    private IEnumerator ShowMessage(string message, float delay)
    {
        CoinsText.text = message;
        yield return new WaitForSeconds(delay);
        CoinsText.text = "Coins: " + userCoins;
    }

    public void PurchasePFP(string pfpID, Button buyButton, Button useButton, string imageURL)
    {
        if (userCoins < 10)
        {
            StartCoroutine(ShowMessage("Not enough coins to buy this PFP!", 2.0f));
            return;
        }

        userCoins -= 10;
        CoinsText.text = "Coins: " + userCoins;

        // Update coins in Firestore
        db.Collection("Users").Document(userID).UpdateAsync(new Dictionary<string, object>
        {
            { "Coins", userCoins }
        });

        if (int.Parse(userID) == 0)
        {
            // Mark as purchased in Firestore
                db.Collection("PFPs").Document(pfpID).UpdateAsync(new Dictionary<string, object>
            {
                { "BlackOwned", false },
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

        } else
        {
            // Mark as purchased in Firestore
            db.Collection("PFPs").Document(pfpID).UpdateAsync(new Dictionary<string, object>
            {
                { "BlackOwned", false },
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
            // Update coins in Firestore
            db.Collection("Users").Document(userID).UpdateAsync(new Dictionary<string, object>
        {
            { "Coins", userCoins }
        });

        //// Mark as purchased in Firestore
        //db.Collection("PFPs").Document(pfpID).UpdateAsync(new Dictionary<string, object>
        //{
        //    { "BlackOwned", true },
        //    { "WhiteOwned", true }
        //}).ContinueWithOnMainThread(task =>
        //{
        //    if (task.IsCompleted)
        //    {
        //        Debug.Log($"PFP {pfpID} purchased!");
        //        buyButton.interactable = false;
        //        useButton.interactable = true;
        //        useButton.onClick.AddListener(() => ApplyPFP(pfpID, imageURL));
        //    }
        //});
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
        StartCoroutine(LoadPFPForPlayer(imageURL, userID));
        syncPfpPicture(imageURL, userID);

        // Sync across all clients
        GameManager.Instance.SetPlayerPFPServerRpc(NetworkManager.Singleton.LocalClientId, pfpID);
    }


    [ServerRpc(RequireOwnership = false)]
    public void syncPfpPicture(string imageurl, string playerid)
    {
        setRespectivePLayerPfp(imageurl, playerid);
    }


    [ClientRpc]
    public void setRespectivePLayerPfp(string imageurl, string playerid)
    {
        Debug.Log($" [Client] Applying PFP {imageurl} for player {playerid}");

        StartCoroutine(LoadPFPForPlayer(imageurl, playerid));
    }

    /// <summary>
    /// Loads and applies the profile picture for the player.
    /// </summary>
    private IEnumerator LoadPFPForPlayer(string url, string playerid)
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
                if (int.Parse(playerid) == 0)
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
        foreach (var player in GameManager.Instance.connectedPlayers)
        {
            db.Collection("Users").Document(player.ToString()).GetSnapshotAsync().ContinueWithOnMainThread(task =>
            {
                Debug.Log($"Loading PFP for player {player}");
                if (task.IsCompleted && task.Result.Exists)
                {
                    if (task.Result.ContainsField("ImageUrl"))
                    {
                        string imageURL = task.Result.GetValue<string>("ImageUrl");

                        if (!string.IsNullOrEmpty(imageURL))
                        {
                            Debug.Log($"Loading stored PFP: {imageURL}");
                            StartCoroutine(LoadPFPForPlayer(imageURL, player.ToString()));
                        }
                    }
                }
            });
        }
    }

    public void ToggleStore()
    {
        if (store.activeSelf)
        {
            StoreBtn.GetComponentInChildren<TMP_Text>().text = "Open DLC Store";

            store.SetActive(false);
        }
        else
        {
            LoadUserCoins();
            LoadPFPs(); 
            StoreBtn.GetComponentInChildren<TMP_Text>().text = "Close DLC Store";
            store.SetActive(true);
        }
    }



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
    /// Loads the saved game state from Firebase and applies it.
    /// </summary>
    public void LoadGameStateFromFirebase()
    {
        db.Collection("Game").Document("0").GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCompleted && task.Result.Exists)
            {
                string loadedGameState = task.Result.GetValue<string>("GameString");
                Debug.Log("Loaded game state: " + loadedGameState);

                // Apply the loaded game state
                GameManager.Instance.LoadGame(loadedGameState);
            }
            else
            {
                Debug.LogError("Failed to load game state from Firebase.");
            }
        });
    }


}
