using System;                          // Provides fundamental classes and base classes.
using UnityEngine;                     // Contains Unity-specific classes.
using Unity.Services.Core;             // Contains core functionality of Unity Services.
using Unity.Services.Analytics;        // Provides classes to work with Unity Analytics.

/// <summary>
/// AnalyticsLogger is a MonoBehaviour that initializes Unity Services,
/// starts the analytics data collection, and provides static methods to log
/// custom analytic events such as match start, match end, and DLC purchases.
/// </summary>
public class AnalyticsLogger : MonoBehaviour
{
    /// <summary>
    /// Awake is called when the script instance is being loaded.
    /// This method initializes Unity Services if they have not been already initialized,
    /// and then starts data collection for analytics.
    /// </summary>
    private async void Awake()
    {
        // Check if Unity Services are not already initialized.
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            try
            {
                // Asynchronously initialize Unity Services.
                await UnityServices.InitializeAsync();
                // Start the analytics data collection.
                AnalyticsService.Instance.StartDataCollection();
                // Log a message indicating successful initialization and start of data collection.
                Debug.Log("[Analytics] Initialized and started data collection.");
            }
            catch (Exception ex)
            {
                // Log an error message if initialization fails.
                Debug.LogError($"[Analytics] Initialization failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Logs a custom analytic event when a match starts.
    /// </summary>
    /// <param name="userId">The user identifier for the match event.</param>
    public static void LogMatchStarted(string userId)
    {
        // Create a new custom event with the event name "match_started".
        CustomEvent evt = new CustomEvent("match_started")
        {
            // Add the user ID to the event data.
            { "user_id", userId }
        };
        // Record the event using the AnalyticsService.
        AnalyticsService.Instance.RecordEvent(evt);
        // Log the event to the console for debugging.
        Debug.Log("[Analytics] Logged: match_started");
    }

    /// <summary>
    /// Logs a custom analytic event when a match ends.
    /// </summary>
    /// <param name="userId">The user identifier for the match event.</param>
    /// <param name="result">The result of the match (e.g., win, loss, or draw).</param>
    public static void LogMatchEnded(string userId, string result)
    {
        // Create a new custom event with the event name "match_ended".
        CustomEvent evt = new CustomEvent("match_ended")
        {
            // Add the user ID to the event data.
            { "user_id", userId },
            // Add the match result to the event data.
            { "result", result }
        };
        // Record the event using the AnalyticsService.
        AnalyticsService.Instance.RecordEvent(evt);
        // Log the event to the console for debugging.
        Debug.Log("[Analytics] Logged: match_ended");
    }

    /// <summary>
    /// Logs a custom analytic event when a DLC is purchased.
    /// </summary>
    /// <param name="userId">The user identifier for the event.</param>
    /// <param name="dlcId">The identifier for the purchased DLC.</param>
    public static void LogDLCPurchased(string userId, string dlcId)
    {
        // Create a new custom event with the event name "dlc_purchase".
        CustomEvent evt = new CustomEvent("dlc_purchase")
        {
            // Add the user ID to the event data.
            { "user_id", userId },
            // Add the DLC identifier to the event data.
            { "dlc_id", dlcId }
        };
        // Record the event using the AnalyticsService.
        AnalyticsService.Instance.RecordEvent(evt);
        // Log the event to the console for debugging.
        Debug.Log("[Analytics] Logged: dlc_purchase");
    }
}
