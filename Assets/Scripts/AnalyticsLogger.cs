using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Services.Analytics;
using Unity.Services.Core;

public class AnalyticsLogger : MonoBehaviour
{
    private async void Awake()
    {
        if (!UnityServices.State.Equals(ServicesInitializationState.Initialized))
        {
            try
            {
                await UnityServices.InitializeAsync();

                // For Unity's new analytics, you also need to check for required consents:
                await AnalyticsService.Instance.CheckForRequiredConsents();

                Debug.Log("[Analytics] Unity Services Initialized and consents checked.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Analytics] Initialization failed: {e.Message}");
            }
        }
    }

    public static void LogMatchStarted(string userId)
    {
        Dictionary<string, object> data = new Dictionary<string, object>
        {
            { "eventType", "match_started" },
            { "userId", userId },
            { "timestamp", DateTime.UtcNow.ToString("o") }
        };
        AnalyticsService.Instance.SendCustomEvent("match_started", data);
        Debug.Log("[Analytics] Match started event logged.");
    }

    public static void LogMatchEnded(string userId, string result)
    {
        Dictionary<string, object> data = new Dictionary<string, object>
        {
            { "eventType", "match_ended" },
            { "userId", userId },
            { "result", result }, // "White Wins", "Black Wins", or "Draw"
            { "timestamp", DateTime.UtcNow.ToString("o") }
        };
        AnalyticsService.Instance.SendCustomEvent("match_ended", data);
        Debug.Log("[Analytics] Match ended event logged.");
    }

    public static void LogDLCPurchased(string userId, string dlcId)
    {
        Dictionary<string, object> data = new Dictionary<string, object>
        {
            { "eventType", "dlc_purchase" },
            { "userId", userId },
            { "dlcId", dlcId },
            { "timestamp", DateTime.UtcNow.ToString("o") }
        };
        AnalyticsService.Instance.SendCustomEvent("dlc_purchase", data);
        Debug.Log("[Analytics] DLC purchase event logged.");
    }
}
