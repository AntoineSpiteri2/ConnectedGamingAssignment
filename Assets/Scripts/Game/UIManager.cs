using Firebase.Extensions;                // Provides Firebase task extensions.
using Firebase.Firestore;                 // Enables Firestore interactions.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using TMPro;                              // For advanced text rendering (TextMeshPro).
using Unity.Netcode;                      // For multiplayer network functionality.
using UnityChess;                         // Likely a custom namespace for chess logic.
using UnityEngine;
using UnityEngine.UI;                     // For Unity UI components.

/// <summary>
/// Manages the user interface of the chess game, including promotion UI, move history,
/// turn indicators, game string serialization, and board information displays.
/// Inherits from MonoBehaviourSingleton to ensure only one instance exists.
/// </summary>
public class UIManager : MonoBehaviourSingleton<UIManager>
{
    // Reference to the promotion UI panel.
    [SerializeField] private GameObject promotionUI = null;
    // Text element to display game result messages (e.g., win, draw).
    [SerializeField] private Text resultText = null;
    // Input field to display and edit the serialized game state string.
    [SerializeField] private InputField GameStringInputField = null;
    // Indicator image to show when it's White's turn.
    [SerializeField] private Image whiteTurnIndicator = null;
    // Indicator image to show when it's Black's turn.
    [SerializeField] private Image blackTurnIndicator = null;
    // Parent GameObject that holds the move history UI elements.
    [SerializeField] private GameObject moveHistoryContentParent = null;
    // Scrollbar for the move history list.
    [SerializeField] private Scrollbar moveHistoryScrollbar = null;
    // Prefab for the full move UI element.
    [SerializeField] private FullMoveUI moveUIPrefab = null;
    // Array of text elements for displaying board information.
    [SerializeField] private Text[] boardInfoTexts = null;
    // Background colour for the move history UI.
    [SerializeField] private Color backgroundColor = new Color(0.39f, 0.39f, 0.39f);
    // Text colour for the board information.
    [SerializeField] private Color textColor = new Color(1f, 0.71f, 0.18f);
    // Darkening factor for button colours (range -0.25 to 0.25).
    [SerializeField, Range(-0.25f, 0.25f)] private float buttonColorDarkenAmount = 0f;
    // Darkening factor for alternate move history row colours (range -0.25 to 0.25).
    [SerializeField, Range(-0.25f, 0.25f)] private float moveHistoryAlternateColorDarkenAmount = 0f;
    // Reference to the DLC store UI panel.
    [SerializeField] private GameObject DlcStore;

    // UI buttons for starting, loading, and saving games.
    [SerializeField] public GameObject startNewGameButton;
    [SerializeField] public GameObject loadGameButton;
    [SerializeField] public GameObject saveGameButtonServer;
    [SerializeField] public GameObject loadGameButtonServer;

    // Timeline to keep track of the full move UI elements in sequence.
    public Timeline<FullMoveUI> moveUITimeline;
    // Button colour computed based on background colour and darkening factor.
    private Color buttonColor;

    // Temporary player identifier. Replace with Firebase Auth for real users.
    public string playerId = "testUser";

    // LateUpdate runs every frame, after all Update() methods.
    void LateUpdate()
    {
        // Check if resultText exists and is currently inactive.
        if (resultText != null && !resultText.gameObject.activeSelf)
        {
            Debug.LogWarning("[FORCE FIX] Re-enabling resultText");
            // Reactivate the result text if it is unexpectedly disabled.
            resultText.gameObject.SetActive(true); // Optional temporary fix.
        }
    }

    // Toggles the DLC Store UI visibility.
    public void ToggleDLCStore()
    {
        DlcStore.SetActive(!DlcStore.activeSelf);
    }

    /// <summary>
    /// Initializes UIManager by subscribing to game events and configuring initial UI settings.
    /// </summary>
    private void Start()
    {
        // Subscribe to game events for new game, game end, moves, and resetting to a half-move.
        GameManager.NewGameStartedEvent += OnNewGameStarted;
        GameManager.GameEndedEvent += OnGameEnded;
        GameManager.MoveExecutedEvent += OnMoveExecuted;
        GameManager.GameResetToHalfMoveEvent += OnGameResetToHalfMove;

        // Initialize the timeline for move UI elements.
        moveUITimeline = new Timeline<FullMoveUI>();

        // Set the color for each board information text element.
        foreach (Text boardInfoText in boardInfoTexts)
        {
            boardInfoText.color = textColor;
        }

        // Calculate the button color by adjusting the background color with the darkening factor.
        buttonColor = new Color(
            backgroundColor.r - buttonColorDarkenAmount,
            backgroundColor.g - buttonColorDarkenAmount,
            backgroundColor.b - buttonColorDarkenAmount
        );
    }

    /// <summary>
    /// Handles the event when a new game starts.
    /// Clears move history, resets UI fields, and updates result text.
    /// </summary>
    private void OnNewGameStarted()
    {
        // Refresh the game string input field with the current serialized game state.
        UpdateGameStringInputField();
        // Update turn indicators based on the current side to move.
        ValidateIndicators();

        // Clear existing move history UI elements.
        for (int i = 0; i < moveHistoryContentParent.transform.childCount; i++)
        {
            Destroy(moveHistoryContentParent.transform.GetChild(i).gameObject);
        }

        // Clear the move timeline.
        moveUITimeline.Clear();

        // Set the result text to indicate that it is White's turn.
        resultText.text = "Its White turn";

        // Make sure the result text is visible.
        resultText.gameObject.SetActive(true);
    }

    /// <summary>
    /// Handles the game-end event (checkmate or stalemate) and displays the outcome.
    /// </summary>
    private void OnGameEnded()
    {
        if (GameManager.Instance.DebugMode) Debug.Log("[UIManager] OnGameEnded event triggered.");

        // Retrieve the most recent half-move.
        if (GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove))
        {
            if (GameManager.Instance.DebugMode) Debug.Log($"[UIManager] Game ended detected. Checkmate: {latestHalfMove.CausedCheckmate}, Stalemate: {latestHalfMove.CausedStalemate}");

            // Determine game outcome based on the last move.
            if (latestHalfMove.CausedCheckmate)
            {
                resultText.text = $"{latestHalfMove.Piece.Owner} Wins!";
                // Update the board turn locally and across the network.
                UpdateBoardTurn(resultText.text);
                UpdateBoardTurnServerRpc(resultText.text);
            }
            else if (latestHalfMove.CausedStalemate)
            {
                resultText.text = "Draw.";
                // Update the board turn locally and across the network.
                UpdateBoardTurn(resultText.text);
                UpdateBoardTurnServerRpc(resultText.text);
            }

            // Make sure the result text is visible.
            resultText.gameObject.SetActive(true);
            if (GameManager.Instance.DebugMode) Debug.Log("[UIManager] Result text updated and shown.");
        }
        else
        {
            if (GameManager.Instance.DebugMode) Debug.LogWarning("[UIManager] Failed to retrieve latest half move.");
        }
    }

    // Server RPC to update the board turn message across networked clients.
    [ServerRpc(RequireOwnership = false)]
    public void UpdateBoardTurnServerRpc(string text)
    {
        // Flag that the game has ended.
        GameManager.Instance.gamehasended.Value = true;
        // Invoke client RPC to update UI.
        updateboardClientRpc(text);
    }

    // Client RPC to update the board turn text on client devices.
    [ClientRpc]
    public void updateboardClientRpc(string text)
    {
        resultText.text = text;
    }

    /// <summary>
    /// Handles a move execution event by updating the game state and UI elements.
    /// </summary>
    private void OnMoveExecuted()
    {
        // Refresh the game string input field to reflect the new game state.
        UpdateGameStringInputField();
        // Get the current side whose turn is next.
        Side sideToMove = GameManager.Instance.SideToMove;
        // Update turn indicator images.
        whiteTurnIndicator.enabled = sideToMove == Side.White;
        blackTurnIndicator.enabled = sideToMove == Side.Black;

        // Retrieve the last half-move from the game.
        GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove lastMove);
        // Add the move to the move history UI; note that we use the complement of the current side.
        AddMoveToHistory(lastMove, sideToMove.Complement());

        // If the move puts the current player in check (but not checkmate), update the result text.
        if (lastMove.CausedCheck && !lastMove.CausedCheckmate)
        {
            resultText.text = $"{sideToMove} is in Check!";
            resultText.gameObject.SetActive(true);
        }
        else if (!lastMove.CausedCheckmate && !lastMove.CausedStalemate)
        {
            // Optionally hide the result text if there is no check.
            // resultText.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Handles the event when the game is reset to a specific half-move.
    /// Updates the game string and syncs the move UI timeline.
    /// </summary>
    private void OnGameResetToHalfMove()
    {
        // Update the game string input field.
        UpdateGameStringInputField();
        // Set the timeline's head index (full move number) based on the latest half-move index.
        moveUITimeline.HeadIndex = GameManager.Instance.LatestHalfMoveIndex / 2;
        // Revalidate turn indicators.
        ValidateIndicators();
    }

    /// <summary>
    /// Enables or disables the promotion UI.
    /// </summary>
    /// <param name="value">True to show the promotion UI; false to hide it.</param>
    public void SetActivePromotionUI(bool value) => promotionUI.gameObject.SetActive(value);

    /// <summary>
    /// Handles the player's selection for piece promotion.
    /// </summary>
    /// <param name="choice">Integer representing the chosen promotion piece.</param>
    public void OnElectionButton(int choice) => GameManager.Instance.ElectPiece((ElectedPiece)choice);

    /// <summary>
    /// Resets the game to the very first half-move.
    /// </summary>
    public void ResetGameToFirstHalfMove() => GameManager.Instance.ResetGameToHalfMoveIndex(0);

    /// <summary>
    /// Resets the game to one half-move before the current state.
    /// </summary>
    public void ResetGameToPreviousHalfMove() => GameManager.Instance.ResetGameToHalfMoveIndex(Math.Max(0, GameManager.Instance.LatestHalfMoveIndex - 1));

    /// <summary>
    /// Advances the game to the next half-move.
    /// </summary>
    public void ResetGameToNextHalfMove() => GameManager.Instance.ResetGameToHalfMoveIndex(Math.Min(GameManager.Instance.LatestHalfMoveIndex + 1, GameManager.Instance.HalfMoveTimeline.Count - 1));

    /// <summary>
    /// Resets the game to the last half-move in the move history timeline.
    /// </summary>
    public void ResetGameToLastHalfMove() => GameManager.Instance.ResetGameToHalfMoveIndex(GameManager.Instance.HalfMoveTimeline.Count - 1);

    /// <summary>
    /// Starts a new game by loading a default serialized game state.
    /// </summary>
    // Serialized string representing a new chess game starting position.
    string serialized = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public void StartNewGame()
    {
        // Ensure the result text and its container are active.
        resultText.gameObject.transform.parent.gameObject.SetActive(true);
        resultText.gameObject.SetActive(true);
        resultText.enabled = true;

        // Only the server should start a new game.
        if (NetworkManager.Singleton.IsServer)
        {
            // Load the new game state.
            GameManager.Instance.LoadGame(serialized);
            resultText.gameObject.SetActive(true);
            // Set the result text to indicate that it is White's turn.
            resultText.text = "Its White turn";
            // Clear any previous move history UI elements.
            for (int i = 0; i < moveHistoryContentParent.transform.childCount; i++)
            {
                Destroy(moveHistoryContentParent.transform.GetChild(i).gameObject);
            }
        }
        else
        {
            // If not the host, show a temporary message stating that only the host can start a new game.
            resultText.gameObject.SetActive(true);
            resultText.gameObject.transform.parent.gameObject.SetActive(true);
            StartCoroutine(DisplayMessageForSeconds("Only the host can start a new game", 3));
        }
    }

    /// <summary>
    /// Clears the board and removes the move history UI.
    /// </summary>
    public void CLearBoardhistory()
    {
        // Clear the board via the BoardManager.
        BoardManager.Instance.ClearBoard();

        // Destroy all move history UI child objects.
        for (int i = 0; i < moveHistoryContentParent.transform.childCount; i++)
        {
            Destroy(moveHistoryContentParent.transform.GetChild(i).gameObject);
        }
    }

    /// <summary>
    /// Loads a game from the serialized game string entered by the user.
    /// </summary>
    public void LoadGame()
    {
        // Ensure the result text and its container are active.
        resultText.gameObject.transform.parent.gameObject.SetActive(true);
        resultText.gameObject.SetActive(true);
        resultText.enabled = true;

        // Only the server can load a game.
        if (NetworkManager.Singleton.IsServer)
        {
            resultText.gameObject.SetActive(true);
            resultText.gameObject.transform.parent.gameObject.SetActive(true);
            // Load the game state provided in the input field.
            GameManager.Instance.LoadGame(GameStringInputField.text);
            // Reset the result text to indicate it's White's turn.
            resultText.text = "Its White turn";
            // Clear previous move history.
            for (int i = 0; i < moveHistoryContentParent.transform.childCount; i++)
            {
                Destroy(moveHistoryContentParent.transform.GetChild(i).gameObject);
            }
        }
        else
        {
            // For non-host players, display a temporary message.
            resultText.gameObject.SetActive(true);
            resultText.gameObject.transform.parent.gameObject.SetActive(true);
            StartCoroutine(DisplayMessageForSeconds("Only the host can load a game", 3));
        }
    }

    /// <summary>
    /// Displays a message for a specific duration, then resets the result text to show the current turn.
    /// </summary>
    /// <param name="message">Temporary message to display.</param>
    /// <param name="seconds">Duration in seconds.</param>
    /// <returns>Coroutine IEnumerator.</returns>
    private IEnumerator DisplayMessageForSeconds(string message, float seconds)
    {
        // Ensure result text and its container are active.
        resultText.gameObject.SetActive(true);
        resultText.gameObject.transform.parent.gameObject.SetActive(true);
        resultText.enabled = true;
        // Set the temporary message.
        resultText.text = message;
        // Wait for the specified time.
        yield return new WaitForSeconds(seconds);
        // After waiting, update result text based on whose turn it is.
        bool isWhiteTurn = GameManager.Instance.isWhiteTurn;
        resultText.text = isWhiteTurn ? "Its White turn" : "Its Black turn";
    }

    /// <summary>
    /// Adds a new move entry to the move history UI using the latest half-move.
    /// </summary>
    /// <param name="latestHalfMove">The most recent half-move executed.</param>
    /// <param name="latestTurnSide">The side (opponent) that executed the move.</param>
    private void AddMoveToHistory(HalfMove latestHalfMove, Side latestTurnSide)
    {
        // Remove any outdated or alternate history entries.
        RemoveAlternateHistory();

        // Depending on which side played the move, update the move history UI.
        switch (latestTurnSide)
        {
            case Side.Black:
                {
                    // For Black moves, if no full move UI exists, instantiate one.
                    if (moveUITimeline.HeadIndex == -1)
                    {
                        FullMoveUI newFullMoveUI = Instantiate(moveUIPrefab, moveHistoryContentParent.transform);
                        moveUITimeline.AddNext(newFullMoveUI);

                        // Place the new UI element at the correct position.
                        newFullMoveUI.transform.SetSiblingIndex(GameManager.Instance.FullMoveNumber - 1);
                        newFullMoveUI.backgroundImage.color = backgroundColor;
                        newFullMoveUI.whiteMoveButtonImage.color = buttonColor;
                        newFullMoveUI.blackMoveButtonImage.color = buttonColor;

                        // Apply an alternate colour for even-numbered moves.
                        if (newFullMoveUI.FullMoveNumber % 2 == 0)
                        {
                            newFullMoveUI.SetAlternateColor(moveHistoryAlternateColorDarkenAmount);
                        }

                        // Set the move number text.
                        newFullMoveUI.MoveNumberText.text = $"{newFullMoveUI.FullMoveNumber}.";
                        // Disable the white move button for this move entry.
                        newFullMoveUI.WhiteMoveButton.enabled = false;
                    }

                    // Retrieve the latest full move UI element.
                    moveUITimeline.TryGetCurrent(out FullMoveUI latestFullMoveUI);
                    // Update the Black move text with the algebraic notation.
                    latestFullMoveUI.BlackMoveText.text = latestHalfMove.ToAlgebraicNotation();
                    // Enable the Black move button.
                    latestFullMoveUI.BlackMoveButton.enabled = true;

                    break;
                }
            case Side.White:
                {
                    // For White moves, instantiate a new full move UI element.
                    FullMoveUI newFullMoveUI = Instantiate(moveUIPrefab, moveHistoryContentParent.transform);
                    newFullMoveUI.transform.SetSiblingIndex(GameManager.Instance.FullMoveNumber - 1);
                    newFullMoveUI.backgroundImage.color = backgroundColor;
                    newFullMoveUI.whiteMoveButtonImage.color = buttonColor;
                    newFullMoveUI.blackMoveButtonImage.color = buttonColor;

                    // Apply alternate colouring if applicable.
                    if (newFullMoveUI.FullMoveNumber % 2 == 0)
                    {
                        newFullMoveUI.SetAlternateColor(moveHistoryAlternateColorDarkenAmount);
                    }

                    // Set the move number text.
                    newFullMoveUI.MoveNumberText.text = $"{newFullMoveUI.FullMoveNumber}.";
                    // Update the White move text with the move notation.
                    newFullMoveUI.WhiteMoveText.text = latestHalfMove.ToAlgebraicNotation();
                    // Clear the Black move text.
                    newFullMoveUI.BlackMoveText.text = "";
                    // Disable the Black move button.
                    newFullMoveUI.BlackMoveButton.enabled = false;
                    // Enable the White move button.
                    newFullMoveUI.WhiteMoveButton.enabled = true;

                    // Add the new UI element to the timeline.
                    moveUITimeline.AddNext(newFullMoveUI);
                    break;
                }
        }

        // Reset the scrollbar to the top of the move history list.
        moveHistoryScrollbar.value = 0;
    }

    /// <summary>
    /// Removes any move history entries that are no longer valid for the current timeline.
    /// </summary>
    private void RemoveAlternateHistory()
    {
        // If the timeline is not up-to-date, remove future (divergent) move entries.
        if (!moveUITimeline.IsUpToDate)
        {
            // Get the latest half-move.
            GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove lastHalfMove);
            // If the last move resulted in a checkmate, ensure the result text is visible.
            resultText.gameObject.SetActive(lastHalfMove.CausedCheckmate);
            // Retrieve the divergent full move UI elements.
            List<FullMoveUI> divergentFullMoveUIs = moveUITimeline.PopFuture();
            // Destroy each divergent UI element.
            foreach (FullMoveUI divergentFullMoveUI in divergentFullMoveUIs)
            {
                Destroy(divergentFullMoveUI.gameObject);
            }
        }
    }

    /// <summary>
    /// Updates the turn indicators based on the current side to move.
    /// </summary>
    public void ValidateIndicators()
    {
        // If GameManager is not available, exit early.
        if (GameManager.Instance == null)
        {
            return;
        }

        // Retrieve whose turn it is.
        Side sideToMove = GameManager.Instance.SideToMove;
        // Enable the white indicator if it's White's turn; otherwise, enable the black indicator.
        whiteTurnIndicator.enabled = (sideToMove == Side.White);
        blackTurnIndicator.enabled = (sideToMove == Side.Black);
    }

    // Updates the board turn text by changing the result text content.
    public void UpdateBoardTurn(string text)
    {
        resultText.gameObject.GetComponent<Text>().text = text;
    }

    /// <summary>
    /// Updates the game string input field with the current serialized game state.
    /// </summary>
    public void UpdateGameStringInputField() => GameStringInputField.text = GameManager.Instance.SerializeGame();
}
