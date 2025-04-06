using System;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Analytics;

public class AnalyticsLogger : MonoBehaviour
{
    private async void Awake()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            try
            {
                await UnityServices.InitializeAsync();
                AnalyticsService.Instance.StartDataCollection();
                Debug.Log("[Analytics] Initialized and started data collection.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Analytics] Initialization failed: {ex.Message}");
            }
        }
    }

    public static void LogMatchStarted(string userId)
    {
        CustomEvent evt = new CustomEvent("match_started")
        {
            { "user_id", userId }
        };
        AnalyticsService.Instance.RecordEvent(evt);
        Debug.Log("[Analytics] Logged: match_started");
    }

    public static void LogMatchEnded(string userId, string result)
    {
        CustomEvent evt = new CustomEvent("match_ended")
        {
            { "user_id", userId },
            { "result", result }
        };
        AnalyticsService.Instance.RecordEvent(evt);
        Debug.Log("[Analytics] Logged: match_ended");
    }

    public static void LogDLCPurchased(string userId, string dlcId)
    {
        CustomEvent evt = new CustomEvent("dlc_purchase")
        {
            { "user_id", userId },
            { "dlc_id", dlcId }
        };
        AnalyticsService.Instance.RecordEvent(evt);
        Debug.Log("[Analytics] Logged: dlc_purchase");
    }
}
