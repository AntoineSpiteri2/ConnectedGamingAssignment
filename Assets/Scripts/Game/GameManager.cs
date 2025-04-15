using System;
using System.Collections.Generic;              // Provides collections like List<T> and Dictionary<TKey,TValue>.
using System.Threading;                           // Provides threading primitives.
using System.Threading.Tasks;                     // Provides Task-based asynchronous programming.
using UnityChess;                                 // Contains chess-specific classes such as Board, Piece, Game, etc.
using UnityEngine;                                // Provides core Unity classes.
using Unity.Netcode;                              // Provides Unity Netcode classes for networking.
using Unity.Collections;                          // Provides Unity collection types with native memory features.
using Unity.Netcode.Components;                   // Contains network components such as NetworkTransform.
using System.Drawing;                             // (May be used for additional graphics functionality; check if needed.)
using System.Collections;                         // For non-generic collection types.
using UnityEngine.UI;                             // Provides UI elements like Button.

/// <summary>
/// Manages the overall game state, including game start, move execution,
/// handling special moves (castling, en passant, promotion), and game resetting.
/// Inherits from NetworkBehaviour so it can participate in Unity Netcode.
/// </summary>
public class GameManager : NetworkBehaviour
{
    // Singleton instance for centralized access.
    public static GameManager Instance { get; private set; }

    /// <summary>
    /// Awake is called when the script instance is loaded.
    /// Initializes the singleton instance.
    /// </summary>
    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    // Debug flag to enable verbose logging.
    public bool DebugMode = false;

    // Events used to signal various game state changes.
    public static event Action NewGameStartedEvent;
    public static event Action GameEndedEvent;
    public static event Action GameResetToHalfMoveEvent;
    public static event Action MoveExecutedEvent;

    // NetworkVariables for player profile picture IDs, replicated to everyone and written by the server.
    public NetworkVariable<FixedString128Bytes> WhitePFP = new NetworkVariable<FixedString128Bytes>("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<FixedString128Bytes> BlackPFP = new NetworkVariable<FixedString128Bytes>("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Gets the current board state from the active game.
    /// Retrieves the board from the board timeline, if available.
    /// </summary>
    public Board CurrentBoard
    {
        get
        {
            // Try to get the current board from the timeline.
            game.BoardTimeline.TryGetCurrent(out Board currentBoard);
            return currentBoard;
        }
    }

    /// <summary>
    /// Gets or sets the side (White/Black) whose turn it is to move.
    /// The setter updates the current game conditions in the timeline.
    /// </summary>
    public Side SideToMove
    {
        get
        {
            // Retrieve current game conditions from the timeline.
            game.ConditionsTimeline.TryGetCurrent(out GameConditions currentConditions);
            return currentConditions.SideToMove;
        }
        set
        {
            // Retrieve existing conditions then create a new GameConditions with the new turn.
            game.ConditionsTimeline.TryGetCurrent(out GameConditions currentConditions);
            currentConditions = new GameConditions(
                value,
                currentConditions.WhiteCanCastleKingside,
                currentConditions.WhiteCanCastleQueenside,
                currentConditions.BlackCanCastleKingside,
                currentConditions.BlackCanCastleQueenside,
                currentConditions.EnPassantSquare,
                currentConditions.HalfMoveClock,
                currentConditions.TurnNumber
            );
            // Update the current head of the conditions timeline.
            game.ConditionsTimeline[game.ConditionsTimeline.HeadIndex] = currentConditions;
        }
    }

    /// <summary>
    /// The side that started the game. This is determined from the initial game conditions.
    /// </summary>
    public Side StartingSide => game.ConditionsTimeline[0].SideToMove;

    /// <summary>
    /// Exposes the timeline of half-moves made during the game.
    /// </summary>
    public Timeline<HalfMove> HalfMoveTimeline => game.HalfMoveTimeline;

    /// <summary>
    /// Returns the index of the most recent half-move.
    /// </summary>
    public int LatestHalfMoveIndex => game.HalfMoveTimeline.HeadIndex;

    /// <summary>
    /// Computes the full move number based on the starting side and half-move count.
    /// Uses a switch expression to handle White and Black starting cases.
    /// </summary>
    public int FullMoveNumber => StartingSide switch
    {
        Side.White => LatestHalfMoveIndex / 2 + 1,
        Side.Black => (LatestHalfMoveIndex + 1) / 2 + 1,
        _ => -1
    };

    // Flags to indicate if an AI is controlling white or black (not used in provided code).
    private bool isWhiteAI;
    private bool isBlackAI;

    /// <summary>
    /// Gets a list of all current pieces along with their board positions.
    /// Iterates over every board square and collects any piece found.
    /// </summary>
    public List<(Square, Piece)> CurrentPieces
    {
        get
        {
            currentPiecesBacking.Clear();
            // Loop through each board square from file 1 to 8 and rank 1 to 8.
            for (int file = 1; file <= 8; file++)
            {
                for (int rank = 1; rank <= 8; rank++)
                {
                    Piece piece = CurrentBoard[file, rank];
                    // If a piece exists, add the square and piece tuple to the backing list.
                    if (piece != null) currentPiecesBacking.Add((new Square(file, rank), piece));
                }
            }
            return currentPiecesBacking;
        }
    }

    // Backing list used internally to store the current piece positions.
    private readonly List<(Square, Piece)> currentPiecesBacking = new List<(Square, Piece)>();

    // Reference to a debug utility for the chess engine (likely for on-screen debugging).
    [SerializeField] private UnityChessDebug unityChessDebug;
    // The main game instance containing all state related to chess.
    public Game game;
    // Serializers for converting game state to/from standard notations.
    private FENSerializer fenSerializer;
    private PGNSerializer pgnSerializer;
    // Cancellation token for handling asynchronous promotion UI tasks.
    public CancellationTokenSource promotionUITaskCancellationTokenSource;
    // TaskCompletionSource for waiting on the user's promotion piece choice.
    private TaskCompletionSource<ElectedPiece> promotionChoiceTCS;
    // Mapping between game serialization types (e.g., FEN, PGN) and their serializers.
    private Dictionary<GameSerializationType, IGameSerializer> serializersByType;
    // The currently selected serialization type; default is FEN.
    private GameSerializationType selectedSerializationType = GameSerializationType.FEN;

    // Dictionary to store pending promotion moves keyed by the starting square.
    private readonly Dictionary<Square, PromotionMove> pendingPromotionMoves = new Dictionary<Square, PromotionMove>();

    // A NetworkVariable to flag whether the game has ended (replicated, written by server).
    public NetworkVariable<bool> gamehasended = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Unity's Start method initialises the game, subscribes to events,
    /// and sets up serializers and UI as needed.
    /// </summary>
    public void Start()
    {
        // Subscribe to visual piece move events.
        VisualPiece.VisualPieceMoved += OnPieceMoved;

        // Initialise the serializers mapping for FEN and PGN.
        serializersByType = new Dictionary<GameSerializationType, IGameSerializer>
        {
            [GameSerializationType.FEN] = new FENSerializer(),
            [GameSerializationType.PGN] = new PGNSerializer()
        };

        // Start a new game immediately.
        StartNewGame();

#if DEBUG_VIEW
        // Activate debug view if compiled with the DEBUG_VIEW flag.
        unityChessDebug.gameObject.SetActive(true);
        unityChessDebug.enabled = true;
#endif
    }

    /// <summary>
    /// Starts a new game by resetting the game state and invoking the NewGameStartedEvent.
    /// </summary>
    public async void StartNewGame()
    {
        // Reset the game ended flag.
        gamehasended.Value = false;
        // Create a new game instance.
        game = new Game();
        // Signal subscribers (e.g., UI) that a new game has started.
        NewGameStartedEvent?.Invoke();
    }

    #region Simple Save/Load of entire game

    /// <summary>
    /// Serialises the current game state using the selected serializer (FEN or PGN).
    /// </summary>
    /// <returns>A string representing the serialised game state.</returns>
    public string SerializeGame()
    {
        if (serializersByType.TryGetValue(selectedSerializationType, out IGameSerializer serializer))
        {
            return serializer.Serialize(game);
        }
        return null;
    }

    /// <summary>
    /// Loads a game from a serialised string, updates the board, and resets the move UI.
    /// </summary>
    /// <param name="serialized">The game state string in FEN format.</param>
    public void LoadGame(string serialized)
    {
        // Deserialize the game state using FEN.
        game = new FENSerializer().Deserialize(serialized);

        // Extract turn information from the serialised data.
        char turnIndicator = serialized.Split(' ')[1][0];
        Side activePlayer = (turnIndicator == 'w') ? Side.White : Side.Black;

        isWhiteTurn = (turnIndicator == 'w');

        // Ensure the turn information is synchronized.
        SideToMove = activePlayer;

        // Clear the move timeline in the UI.
        UIManager.Instance.moveUITimeline.Clear();

        if (DebugMode) Debug.Log($"[GameManager] Loaded game with active player: {activePlayer}");

        // Clear the board visuals.
        BoardManager.Instance.ClearBoard();

        // Re-create visual pieces based on the current board state.
        foreach ((Square position, Piece piece) in GameManager.Instance.CurrentPieces)
        {
            BoardManager.Instance.CreateAndPlacePieceGO(piece, position);
        }

        // Notify clients about the turn and update UI indicators.
        NotifyTurnChangeClientRpc(isWhiteTurn);
        UpdateTurnIndicatorClientRpc(isWhiteTurn);
        UIManager.Instance.ValidateIndicators();
    }

    #endregion

    // ---------------------- Server / Client RPCs and Networking --------------------

    /// <summary>
    /// Server RPC to sync a piece's name across the network.
    /// Resets the gameend flag and then invokes a client RPC.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SyncPieceNameServerRpc(ulong networkObjectId, string pieceName)
    {
        GameManager.Instance.gamehasended.Value = false;
        SyncPieceNameClientRpc(networkObjectId, pieceName);
    }

    /// <summary>
    /// Client RPC to update the piece's name after it has been spawned.
    /// Uses a coroutine to ensure the object exists before renaming.
    /// </summary>
    [ClientRpc]
    private void SyncPieceNameClientRpc(ulong networkObjectId, string pieceName)
    {
        StartCoroutine(EnsureObjectAndRename(networkObjectId, pieceName));
    }

    // Coroutine that waits until the network object is spawned before renaming it.
    private IEnumerator EnsureObjectAndRename(ulong networkObjectId, string pieceName)
    {
        GameObject pieceObject = null;
        // Wait until the object exists on the client.
        while (pieceObject == null)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
            {
                pieceObject = netObj.gameObject;
            }
            yield return null; // Wait for the next frame.
        }
        pieceObject.name = pieceName; // Assign the correct name.
    }

    /// <summary>
    /// Resets the game state to a specific half-move index.
    /// Cancels any pending promotion tasks and notifies subscribers.
    /// </summary>
    /// <param name="halfMoveIndex">Index of the half-move to revert to.</param>
    public void ResetGameToHalfMoveIndex(int halfMoveIndex)
    {
        // If resetting the game fails, abort.
        if (!game.ResetGameToHalfMoveIndex(halfMoveIndex)) return;

        // Disable promotion UI and cancel pending tasks.
        UIManager.Instance.SetActivePromotionUI(false);
        promotionUITaskCancellationTokenSource?.Cancel();
        // Notify subscribers that the game state has been reverted.
        GameResetToHalfMoveEvent?.Invoke();
    }

    /// <summary>
    /// Attempts to execute a move within the game logic.
    /// Handles special moves and updates turn order.
    /// </summary>
    /// <param name="move">The movement to execute.</param>
    /// <returns>True if move execution was successful; otherwise, false.</returns>
    private bool TryExecuteMove(Movement move)
    {
        // Validate and perform the move in the game logic.
        if (!game.TryExecuteMove(move)) return false;

        // Check for end-of-game conditions: checkmate or stalemate.
        HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);
        GetNumLegalMovesForCurrentPosition();

        if (latestHalfMove.CausedCheckmate || latestHalfMove.CausedStalemate)
        {
            // Log match end with analytics.
            bool isWhiteWin = latestHalfMove.CausedCheckmate && latestHalfMove.Piece.Owner == Side.White;
            AnalyticsLogger.LogMatchEnded(DLCManager.Instance.userID, isWhiteWin ? "White" : "Black");

            if (DebugMode) Debug.Log("[GameManager] Game End condition met. Invoking GameEndedEvent.");

            // Signal game end to clients.
            DeclareGameEndServerRpc(latestHalfMove.CausedCheckmate, isWhiteWin: isWhiteWin);
        }
        else
        {
            // Update piece interactivity for the next turn.
            BoardManager.Instance.EnsureOnlyPiecesOfSideAreEnabled(SideToMove);
            MoveExecutedEvent?.Invoke();
            // Switch the turn.
            SideToMove = (SideToMove == Side.White) ? Side.Black : Side.White;
        }
        return true;
    }

    /// <summary>
    /// Allows the user to elect a promotion piece.
    /// Called by UI when a promotion choice is made.
    /// </summary>
    /// <param name="choice">The elected promotion piece.</param>
    public void ElectPiece(ElectedPiece choice)
    {
        promotionChoiceTCS?.TrySetResult(choice);
    }

    /// <summary>
    /// Event handler invoked when a visual piece is moved.
    /// Packages move data into a DTO, serialises it, and requests server move validation.
    /// </summary>
    /// <param name="movedPieceInitialSquare">Initial square from which the piece moved.</param>
    /// <param name="movedPieceTransform">Transform of the moved piece.</param>
    /// <param name="droppedSquareTransform">Transform of the destination square.</param>
    /// <param name="promotionPiece">Optional promotion piece if applicable.</param>
    private void OnPieceMoved(Square startSquare,
                              Transform movedPieceTransform,
                              Transform droppedSquareTransform,
                              Piece promotionPiece)
    {
        // Build a MoveDTO containing all relevant move data.
        MoveDTO moveDTO = new MoveDTO
        {
            initialSquare = new SerializableSquare(startSquare.File, startSquare.Rank),
            pieceTransformName = movedPieceTransform.name,
            destinationSquareTransformName = droppedSquareTransform.name,
            promotionPieceType = promotionPiece != null ? promotionPiece.GetType().Name : null
        };

        // Convert the move data to JSON for network transmission.
        string moveJson = JsonUtility.ToJson(moveDTO);
        // Get the local client ID.
        ulong clientId = NetworkManager.Singleton.LocalClientId;
        // Request the server to validate the move.
        RequestMoveValidationServerRpc(moveJson, clientId);
    }

    /// <summary>
    /// Determines whether a given piece has any legal moves in the current game state.
    /// </summary>
    /// <param name="piece">The chess piece to evaluate.</param>
    /// <returns>True if at least one legal move exists; false otherwise.</returns>
    public bool HasLegalMoves(Piece piece)
    {
        return game.TryGetLegalMovesForPiece(piece, out _);
    }

    // --------------------- Server-Side Code ---------------------

    // Backing list for pieces that have been captured.
    private List<SerializableSquare> destroyedPieces = new List<SerializableSquare>();

    // Tracks whose turn it is via a simple boolean flag.
    public bool isWhiteTurn = true;

    /// <summary>
    /// Returns the local client ID from the network manager.
    /// </summary>
    public ulong LocalClientId => NetworkManager.Singleton.LocalClientId;

    // List to store connected player IDs. Typically, [0] is White and [1] is Black.
    public List<ulong> connectedPlayers = new List<ulong>();

    // NetworkVariable to store and update the client ping value.
    private NetworkVariable<float> clientPing = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Called when the network spawns the object.
    /// Sets up connection callbacks and initialises player profile picture values.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            if (DebugMode) Debug.Log("GameManager initialized on the server.");
            // Subscribe to client connection and disconnection events.
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            // Set up periodic ping requests.
            InvokeRepeating(nameof(SendPingRequest), 1f, 15f); // Ping every 15 seconds
            WhitePFP.Value = "";
            BlackPFP.Value = "";

            // Enable relevant UI buttons on the server.
            UIManager.Instance.startNewGameButton.gameObject.GetComponent<Button>().interactable = true;
            UIManager.Instance.loadGameButton.gameObject.GetComponent<Button>().interactable = true;
            UIManager.Instance.saveGameButtonServer.gameObject.GetComponent<Button>().interactable = true;
            UIManager.Instance.loadGameButtonServer.gameObject.GetComponent<Button>().interactable = true;
        }
        else
        {
            // For clients, subscribe to disconnection events.
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    /// <summary>
    /// Server RPC to set the profile picture (PFP) for a player based on their client ID.
    /// Player at slot 0 becomes White, slot 1 becomes Black.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SetPlayerPFPServerRpc(ulong clientID, string pfpID)
    {
        if (connectedPlayers.Count >= 2)
        {
            if (connectedPlayers[0] == clientID)
            {
                WhitePFP.Value = pfpID;
            }
            else if (connectedPlayers[1] == clientID)
            {
                BlackPFP.Value = pfpID;
            }
        }
    }

    /// <summary>
    /// Server method to send a ping request to all clients to measure latency.
    /// </summary>
    private void SendPingRequest()
    {
        if (IsServer)
        {
            float timestamp = Time.time;
            RequestPingClientRpc(timestamp);
        }
    }

    /// <summary>
    /// Client RPC that, when received, prompts the client to respond with a ping.
    /// </summary>
    [ClientRpc]
    private void RequestPingClientRpc(float timestamp, ClientRpcParams clientRpcParams = default)
    {
        if (IsClient)
        {
            RespondPingServerRpc(timestamp);
        }
    }

    /// <summary>
    /// Server RPC that processes the client's ping response.
    /// Calculates round-trip time and updates the client ping NetworkVariable.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RespondPingServerRpc(float timestamp, ServerRpcParams serverRpcParams = default)
    {
        if (IsServer)
        {
            float roundTripTime = (Time.time - timestamp) * 1000f; // Convert to milliseconds.
            ulong clientId = serverRpcParams.Receive.SenderClientId;
            // Update the ping value.
            clientPing.Value = roundTripTime;
            // Send the updated ping to all clients.
            UpdatePingClientRpc(clientId, roundTripTime);
        }
    }

    /// <summary>
    /// Client RPC to update the displayed ping in the UI.
    /// </summary>
    [ClientRpc]
    private void UpdatePingClientRpc(ulong clientId, float ping)
    {
        if (UIManager.Instance != null)
        {
            Debug.Log($"[CLIENT] Updated Ping for Client {clientId}: {ping:F1} ms");
        }
    }

    // Set to track client IDs that have connected previously.
    private HashSet<ulong> hasConnectedBefore = new HashSet<ulong>();
    // Dictionary mapping game sides (White, Black) to client IDs.
    private Dictionary<Side, ulong> sideToClientId = new Dictionary<Side, ulong>();

    /// <summary>
    /// Callback invoked when a client connects.
    /// Assigns the client to a player slot and loads the game state.
    /// </summary>
    private void OnClientConnected(ulong clientId)
    {
        // Assign player slot: if a slot is empty, assign the client to that side.
        if (!sideToClientId.ContainsValue(clientId))
        {
            if (!sideToClientId.ContainsKey(Side.White))
                sideToClientId[Side.White] = clientId;
            else if (!sideToClientId.ContainsKey(Side.Black))
                sideToClientId[Side.Black] = clientId;
        }

        // Adjust the clientId and DLCManager's userID based on simple remapping logic.
        if (int.Parse(clientId.ToString()) > 1)
        {
            DLCManager.Instance.userID = "1";
            clientId = 1;
        }
        else
        {
            if (int.Parse(clientId.ToString()) == 1)
            {
                DLCManager.Instance.userID = "1";
                clientId = 1;
            }
            else
            {
                DLCManager.Instance.userID = "0";
                clientId = 0;
            }
        }

        if (DebugMode) Debug.Log($"[SERVER] Client connected with ID: {clientId}");

        // Assign the client to the appropriate slot (White or Black).
        if (!connectedPlayers.Contains(clientId))
        {
            // Check slot 0 (White) first.
            if (connectedPlayers.Count == 0 ||
                !NetworkManager.Singleton.ConnectedClients.ContainsKey(connectedPlayers[0]))
            {
                if (connectedPlayers.Count == 0)
                    connectedPlayers.Insert(0, clientId);
                else
                    connectedPlayers[0] = clientId;

                if (DebugMode) Debug.Log($"[SERVER] Assigned {clientId} to White slot.");
            }
            // Then assign to slot 1 (Black).
            else if (connectedPlayers.Count == 1 ||
                     !NetworkManager.Singleton.ConnectedClients.ContainsKey(connectedPlayers[1]))
            {
                if (connectedPlayers.Count == 1)
                    connectedPlayers.Insert(1, clientId);
                else
                    connectedPlayers[1] = clientId;

                if (DebugMode) Debug.Log($"[SERVER] Assigned {clientId} to Black slot.");
            }
            else
            {
                if (DebugMode) Debug.LogWarning($"[SERVER] More than 2 players tried to connect. Rejecting client {clientId}.");
                return; // Too many players; ignore extra connections.
            }
        }

        // Load the initial game state.
        LoadGame("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        // Broadcast updated connected player slots to all clients.
        UpdateConnectedPlayersClientRpc(connectedPlayers.ToArray());

        if (IsServer && connectedPlayers.Count >= 2)
        {
            AnalyticsLogger.LogMatchStarted(DLCManager.Instance.userID);
            if (DebugMode) Debug.Log("Two players connected, game ready.");
        }
    }

    /// <summary>
    /// Client RPC to update the connected players list on all clients.
    /// Also sets user IDs based on the player slot.
    /// </summary>
    [ClientRpc]
    private void UpdateConnectedPlayersClientRpc(ulong[] playerIds)
    {
        connectedPlayers = new List<ulong>(playerIds); // [0] = White, [1] = Black

        ulong localId = GameManager.Instance.LocalClientId;

        if (playerIds.Length > 0 && localId == playerIds[0])
        {
            DLCManager.Instance.userID = "0"; // White
        }
        else if (playerIds.Length > 1 && localId == playerIds[1])
        {
            DLCManager.Instance.userID = "1"; // Black
        }
        else
        {
            DLCManager.Instance.userID = "1"; // Fallback in unexpected cases.
        }

        if (DebugMode) Debug.Log($"[CLIENT] DLC userID set to {DLCManager.Instance.userID}");
        // Load the user's selected profile picture.
        DLCManager.Instance.LoadUserSelectedPFP();
    }

    /// <summary>
    /// Callback invoked when a client disconnects.
    /// Handles removal from connected players, board clearing, and shutting down the network if needed.
    /// </summary>
    private void OnClientDisconnected(ulong clientId)
    {
        // Remove the disconnected client from the connected players list.
        connectedPlayers.Remove(clientId);
        if (DebugMode) Debug.Log($"[Server] Player {clientId} disconnected.");

        UIManager.Instance.CLearBoardhistory();

        // If the host disconnects (clientId equals 0 after remapping), shut down the network.
        if (int.Parse(clientId.ToString()) == 0)
        {
            if (DebugMode) Debug.Log("Host disconnected! Resetting game...");
            NetworkManager.Singleton.Shutdown();
            // Show the network UI panel for reconnection.
            if (NetworkUI.Instance != null)
            {
                NetworkUI.Instance.Panel.SetActive(true);
            }
            return;
        }

        // For other clients disconnecting:
        if (int.Parse(clientId.ToString()) >= 1)
        {
            if (DebugMode) Debug.Log("Local client disconnected, shutting down and returning to menu...");
            NetworkManager.Singleton.Shutdown();
            if (NetworkUI.Instance != null)
            {
                NetworkUI.Instance.Panel.SetActive(true);
            }
            return;
        }
    }

    /// <summary>
    /// Maps a piece type string ("Queen", "Knight", etc.) to its corresponding ElectedPiece enum.
    /// Provides a default fallback (Queen) if the piece type is unrecognized.
    /// </summary>
    private ElectedPiece ConvertElectedPieceType(string pieceTypeName)
    {
        return pieceTypeName switch
        {
            "Queen" => ElectedPiece.Queen,
            "Rook" => ElectedPiece.Rook,
            "Bishop" => ElectedPiece.Bishop,
            "Knight" => ElectedPiece.Knight,
            _ => ElectedPiece.Queen // Fallback default.
        };
    }

    /// <summary>
    /// Ends the current turn by toggling the turn flag and updating the SideToMove.
    /// Notifies clients to update turn indicators.
    /// </summary>
    public void EndTurn()
    {
        if (gamehasended.Value)
        {
            return; // Do nothing if the game is over.
        }

        isWhiteTurn = !isWhiteTurn;
        SideToMove = isWhiteTurn ? Side.White : Side.Black;
        // Inform clients of the new turn.
        NotifyTurnChangeClientRpc(isWhiteTurn);
        UpdateTurnIndicatorClientRpc(isWhiteTurn);
    }

    /// <summary>
    /// Client RPC that updates the turn indicator text in the UI.
    /// </summary>
    [ClientRpc]
    private void UpdateTurnIndicatorClientRpc(bool whiteTurn)
    {
        if (whiteTurn)
        {
            UIManager.Instance.UpdateBoardTurn("Its White turn");
        }
        else
        {
            UIManager.Instance.UpdateBoardTurn("Its Black turn");
        }
        UIManager.Instance.ValidateIndicators();
    }

    /// <summary>
    /// Client RPC that notifies clients about a turn change.
    /// Forces local variables to update and refreshes the UI.
    /// </summary>
    [ClientRpc]
    private void NotifyTurnChangeClientRpc(bool whiteTurn)
    {
        if (gamehasended.Value)
        {
            return;
        }
        if (DebugMode) Debug.Log($"[CLIENT] Received Turn Change - Expected Turn: {(whiteTurn ? "White's Turn" : "Black's Turn")}");
        isWhiteTurn = whiteTurn;
        SideToMove = isWhiteTurn ? Side.White : Side.Black;
        if (DebugMode) Debug.Log($"[CLIENT] Forced SideToMove Update - Now: {(SideToMove == Side.White ? "White" : "Black")}");
        UIManager.Instance.ValidateIndicators();
    }

    /// <summary>
    /// Server RPC that declares the end of the game (checkmate or draw)
    /// and notifies all clients.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void DeclareGameEndServerRpc(bool isCheckmate, bool isWhiteWin)
    {
        NotifyGameEndClientRpc(isCheckmate, isWhiteWin);
    }

    /// <summary>
    /// Client RPC that informs clients of the game result.
    /// Updates the UI and disables piece interactions.
    /// </summary>
    [ClientRpc]
    private void NotifyGameEndClientRpc(bool isCheckmate, bool isWhiteWin)
    {
        string resultMessage = isCheckmate ? (isWhiteWin ? "White Wins!" : "Black Wins!") : "Draw!";
        UIManager.Instance.UpdateBoardTurn(resultMessage);
        BoardManager.Instance.SetActiveAllPieces(false);
        GameEndedEvent?.Invoke();
    }

    /// <summary>
    /// Server RPC that receives a move request in JSON form and the client ID.
    /// Validates the move, handles promotion if necessary, and executes or rejects the move.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestMoveValidationServerRpc(string moveJson, ulong clientid)
    {
        if (DebugMode) Debug.Log($"[SERVER] Received move JSON: {moveJson}");
        // Deserialize the move JSON into a MoveDTO.
        MoveDTO moveData = JsonUtility.FromJson<MoveDTO>(moveJson);
        Square startSquare = moveData.initialSquare.ToSquare();
        // Find the destination square GameObject by name.
        GameObject endObj = GameObject.Find(moveData.destinationSquareTransformName);
        if (!endObj)
        {
            if (DebugMode) Debug.LogWarning("[Server] Destination transform not found. Rejecting move.");
            RejectMoveClientRpc(moveJson);
            return;
        }
        Square endSquare = new Square(endObj.name);

        // Validate if the move is legal in the current game position.
        if (!game.TryGetLegalMove(startSquare, endSquare, out Movement move))
        {
            if (DebugMode) Debug.LogWarning("[Server] Move was invalid according to the chess logic. Rejecting...");
            RejectMoveClientRpc(moveJson);
            return;
        }

        // If the move is a promotion move, handle the promotion piece assignment.
        PromotionMove promoMove = move as PromotionMove;
        if (move is PromotionMove promoMove_)
        {
            if (!string.IsNullOrEmpty(moveData.promotionPieceType))
            {
                // Generate the promotion piece based on the client's input.
                Piece finalPiece = PromotionUtil.GeneratePromotionPiece(
                    ConvertElectedPieceType(moveData.promotionPieceType),
                    SideToMove
                );
                promoMove.SetPromotionPiece(finalPiece);
            }
        }

        // Handle special moves (castling, en passant, promotion) if needed.
        if (promoMove is SpecialMove specialMove)
        {
            if (DebugMode) Debug.Log($"[Server] Received PromotionMove for {startSquare}->{endSquare}, but no piece chosen yet.");
            // Queue the promotion move until the client picks a piece.
            pendingPromotionMoves[startSquare] = promoMove;
            ClientRpcParams targetSender = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientid } }
            };
            AskPromotionChoiceClientRpc(moveJson, targetSender);
            return; // Do not finalize move until promotion choice is received.
        }

        // For other special moves, attempt to handle their specific logic.
        if (move is SpecialMove specialMove_ && !TryHandleSpecialMoveBehaviourServer(specialMove_))
        {
            if (DebugMode) Debug.Log("[Server] Special move handling failed. Rejecting...");
            RejectMoveClientRpc(moveJson);
            return;
        }

        // Execute the move in the chess game logic.
        if (!TryExecuteMove(move))
        {
            if (DebugMode) Debug.LogWarning("[Server] Move could not be executed. Rejecting...");
            RejectMoveClientRpc(moveJson);
            return;
        }

        // If move execution is successful, broadcast the move to all clients for visual update.
        ValidateAndExecuteMoveClientRpc(moveJson);
    }

    /// <summary>
    /// Server-side method to handle special moves such as castling, en passant, and promotion.
    /// Returns true if the special move handling is successful.
    /// </summary>
    private bool TryHandleSpecialMoveBehaviourServer(SpecialMove specialMove)
    {
        switch (specialMove)
        {
            case CastlingMove castlingMove:
                // Castling involves moving the rook; assume success.
                return true;

            case EnPassantMove epMove:
                // For en passant, track the captured pawn's square.
                SerializableSquare capturedPawnSquare = SerializableSquare.FromSquare(epMove.CapturedPawnSquare);
                destroyedPieces.Add(capturedPawnSquare);
                return true;

            case PromotionMove promoMove:
                // For promotion, if no promotion piece is set, we cannot proceed.
                if (promoMove.PromotionPiece == null)
                {
                    return false;
                }
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to locate and destroy a visual piece by its name. Only the server can do this.
    /// </summary>
    /// <param name="pieceName">The unique name of the piece to be destroyed.</param>
    /// <returns>True if the piece is found and destroyed; otherwise, false.</returns>
    public bool TryDestroyVisualPieceByName(string pieceName)
    {
        if (!IsServer)
        {
            if (DebugMode) Debug.LogWarning("Only the server can destroy pieces.");
            return false;
        }
        GameObject pieceObj = GameObject.Find(pieceName);
        if (pieceObj != null)
        {
            ulong networkObjectId = pieceObj.GetComponent<NetworkObject>().NetworkObjectId;
            DestroyPieceServerRpc(networkObjectId);
            return true;
        }
        if (DebugMode) Debug.LogWarning($"Could not find piece with name: {pieceName} to destroy.");
        return false;
    }

    /// <summary>
    /// Server RPC to despawn (destroy) a networked piece given its network object ID.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void DestroyPieceServerRpc(ulong networkObjectId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
        {
            netObj.Despawn();
        }
    }

    /// <summary>
    /// Client RPC that performs visual updates to reflect an executed move.
    /// Handles normal moves, captures, and promotions by updating or re-instantiating pieces.
    /// </summary>
    [ClientRpc]
    private void ValidateAndExecuteMoveClientRpc(string moveJson)
    {
        MoveDTO moveData = JsonUtility.FromJson<MoveDTO>(moveJson);

        // Retrieve start square from the move data.
        Square startSquare = moveData.initialSquare.ToSquare();
        // Get the piece from the game model at the start square.
        Piece piece = CurrentBoard[startSquare];

        // Find the GameObject representing the piece.
        GameObject pieceObj = GameObject.Find(moveData.pieceTransformName);
        if (!pieceObj)
        {
            if (DebugMode) Debug.LogWarning("[Client] Could not find piece to move. Possibly out of sync?");
            return;
        }
        // Find the destination square GameObject.
        GameObject endObj = GameObject.Find(moveData.destinationSquareTransformName);
        if (!endObj)
        {
            if (DebugMode) Debug.LogWarning("[Client] Could not find end transform. Possibly out of sync?");
            return;
        }
        // Determine the destination square.
        Square endSquare = new Square(endObj.name);
        // If there is a piece at the destination, attempt to remove it (capture).
        BoardManager.Instance.TryDestroyVisualPiece(endSquare);

        // Handle promotion: if the move includes a promotion piece type,
        // remove the old piece and spawn the promoted piece.
        if (!string.IsNullOrEmpty(moveData.promotionPieceType))
        {
            BoardManager.Instance.TryDestroyVisualPiece(startSquare);
            BoardManager.Instance.TryDestroyVisualPiece(endSquare);
            if (DebugMode) Debug.Log("spawning a piece!");
            Piece newPiece = PromotionUtil.GeneratePromotionPiece(
                ConvertElectedPieceType(moveData.promotionPieceType),
                SideToMove
            );
            BoardManager.Instance.CreateAndPlacePieceGO(newPiece, endSquare);
            GameObject newPieceGO = BoardManager.Instance.GetPieceGOAtPosition(endSquare);
            GameObject endSquareGO = GameObject.Find(moveData.destinationSquareTransformName);
            GameManager.Instance.CurrentBoard[endSquare] = newPiece;
        }
        else
        {
            // For normal moves, simply re-parent the piece's GameObject to the destination square.
            pieceObj.transform.SetParent(endObj.transform);
            pieceObj.transform.position = endObj.transform.position;
        }
        // End the turn and update the UI indicators.
        EndTurn();
        UIManager.Instance.ValidateIndicators();
    }

    /// <summary>
    /// Client RPC to revert a move (if rejected) by snapping the piece back to its original square.
    /// </summary>
    [ClientRpc]
    private void RejectMoveClientRpc(string JASON)
    {
        MoveDTO moveData = JsonUtility.FromJson<MoveDTO>(JASON);
        GameObject pieceObj = GameObject.Find(moveData.pieceTransformName);
        if (!pieceObj) return;
        // Reset the piece's position to match its parent's.
        pieceObj.transform.position = pieceObj.transform.parent.position;
    }

    /// <summary>
    /// Adds a destroyed piece's position to the list of captured pieces.
    /// </summary>
    /// <param name="position">The square where the piece was captured.</param>
    public void AddDestroyedPiece(Square position)
    {
        destroyedPieces.Add(new SerializableSquare(position.File, position.Rank));
    }

    /// <summary>
    /// Server RPC to despawn a piece given its network object ID (alias for DestroyPieceServerRpc).
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void DestoryPieceServerRpc(ulong networkObjectId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
        {
            netObj.Despawn();
        }
    }

    /// <summary>
    /// Client RPC that prompts the client to choose a promotion piece.
    /// Activates the promotion UI and disables board pieces until a choice is made.
    /// </summary>
    [ClientRpc]
    private void AskPromotionChoiceClientRpc(string moveJson, ClientRpcParams clientRpcParams = default)
    {
        UIManager.Instance.SetActivePromotionUI(true);
        BoardManager.Instance.SetActiveAllPieces(false);

        // Cancel any pending promotion UI tasks.
        promotionUITaskCancellationTokenSource?.Cancel();
        promotionUITaskCancellationTokenSource = new CancellationTokenSource();

        if (DebugMode) Debug.Log("[Client] The server wants us to pick a promotion piece!");

        HandlePromotionChoiceAsync(moveJson);
    }

    /// <summary>
    /// Asynchronously waits for the user to select a promotion piece.
    /// Once chosen, submits the choice back to the server.
    /// </summary>
    private async void HandlePromotionChoiceAsync(string moveJson)
    {
        if (DebugMode) Debug.Log("[Client] The server wants us to pick a promotion piece!");

        // Wait for the promotion piece choice asynchronously.
        promotionChoiceTCS = new TaskCompletionSource<ElectedPiece>();
        ElectedPiece choice = await promotionChoiceTCS.Task;

        // Reset promotion UI and re-enable board pieces.
        UIManager.Instance.SetActivePromotionUI(false);
        BoardManager.Instance.SetActiveAllPieces(true);

        string chosenPieceType = choice.ToString();
        // Submit the promotion choice to the server.
        SubmitPromotionChoiceServerRpc(moveJson, chosenPieceType);
    }

    /// <summary>
    /// Server RPC that receives the client's promotion choice,
    /// finalizes the promotion move, and executes it.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SubmitPromotionChoiceServerRpc(string moveJson, string chosenPieceType, ServerRpcParams rpcParams = default)
    {
        // Parse the move data.
        MoveDTO moveData = JsonUtility.FromJson<MoveDTO>(moveJson);
        Square startSquare = moveData.initialSquare.ToSquare();

        // Retrieve the pending promotion move.
        if (!pendingPromotionMoves.TryGetValue(startSquare, out PromotionMove promoMove))
        {
            if (DebugMode) Debug.LogWarning("[Server] No pending promotion move found for " + startSquare);
            RejectMoveClientRpc(moveJson);
            return;
        }

        // Generate the final promotion piece based on the client's choice.
        Piece finalPiece = PromotionUtil.GeneratePromotionPiece(
            ConvertElectedPieceType(chosenPieceType),
            SideToMove
        );
        promoMove.SetPromotionPiece(finalPiece);

        // Remove the pending promotion entry.
        pendingPromotionMoves.Remove(startSquare);

        // Handle any remaining special move behaviours.
        if (!TryHandleSpecialMoveBehaviourServer(promoMove))
        {
            if (DebugMode) Debug.LogWarning("[Server] special move handling failed for promotion. Rejecting...");
            RejectMoveClientRpc(moveJson);
            return;
        }

        // Finalize the move execution.
        if (!TryExecuteMove(promoMove))
        {
            if (DebugMode) Debug.LogWarning("[Server] Move could not be executed. Rejecting...");
            RejectMoveClientRpc(moveJson);
            return;
        }

        // Update the move data to include the chosen promotion piece and broadcast to clients.
        moveData.promotionPieceType = chosenPieceType;
        string finalJson = JsonUtility.ToJson(moveData);
        ValidateAndExecuteMoveClientRpc(finalJson);
    }

    /// <summary>
    /// Client RPC that updates the game state from the server-provided serialised data.
    /// Refreshes local game data and UI.
    /// </summary>
    [ClientRpc]
    public void UpdateGameStateClientRpc(string serializedGameState)
    {
        GameManager.Instance.LoadGame(serializedGameState);
        UIManager.Instance.UpdateGameStringInputField();
    }

    /// <summary>
    /// Client RPC that instructs clients to set the parent of a networked object.
    /// Necessary when Unity Netcode does not automatically propagate parent changes.
    /// </summary>
    [ClientRpc]
    public void RequestSetParentClientRpc(ulong childId, ulong parentId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(childId, out NetworkObject childNetObj) &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(parentId, out NetworkObject parentNetObj))
        {
            // Manually set the network object's parent.
            childNetObj.transform.SetParent(parentNetObj.transform, true);
            if (DebugMode) Debug.Log($"[Client] Successfully set parent of {childNetObj.gameObject.name} to {parentNetObj.gameObject.name}");
        }
        else
        {
            if (DebugMode) Debug.LogWarning("[Client] Could not set parent! Objects not found.");
        }
    }

    /// <summary>
    /// Calculates and returns the number of legal moves available for the king.
    /// Primarily used for debugging purposes.
    /// </summary>
    public int GetNumLegalMovesForCurrentPosition()
    {
        // Retrieve current board and conditions.
        game.BoardTimeline.TryGetCurrent(out Board currentBoard);
        game.ConditionsTimeline.TryGetCurrent(out GameConditions currentConditions);

        // Calculate legal moves for the current position.
        var legalMovesByPiece = Game.CalculateLegalMovesForPosition(currentBoard, currentConditions);

        if (DebugMode) Debug.Log("[DEBUG] Listing legal moves for the king:");

        int kingMoveCount = 0;
        foreach (var pieceMoves in legalMovesByPiece)
        {
            Piece piece = pieceMoves.Key;
            // Only consider moves for the king.
            if (piece is King)
            {
                foreach (var move in pieceMoves.Value)
                {
                    if (DebugMode) Debug.Log($"[DEBUG] King at {move.Key.Item1} can move to {move.Key.Item2}");
                    kingMoveCount++;
                }
            }
        }

        if (DebugMode) Debug.Log($"[DEBUG] Total counted legal moves for the king: {kingMoveCount}");
        return kingMoveCount;
    }
}
