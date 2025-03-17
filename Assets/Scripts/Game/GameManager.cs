using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityChess;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using Unity.Netcode.Components;
using System.Drawing;
using System.Collections;


/// <summary>
/// Manages the overall game state, including game start, moves execution,
/// special moves handling (such as castling, en passant, and promotion), and game reset.
/// Inherits from a singleton base class to ensure a single instance throughout the application.
/// </summary>
public class GameManager : NetworkBehaviour
{

    public static GameManager Instance { get; private set; }
    private void Awake()
    {

        if (Instance == null) Instance = this;

    }

    public bool DebugMode = false;

    // Events signalling various game state changes.
    public static event Action NewGameStartedEvent;
    public static event Action GameEndedEvent;
    public static event Action GameResetToHalfMoveEvent;
    
    public static event Action MoveExecutedEvent;

    public NetworkVariable<FixedString128Bytes> WhitePFP = new NetworkVariable<FixedString128Bytes>("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<FixedString128Bytes> BlackPFP = new NetworkVariable<FixedString128Bytes>("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);


    /// <summary>
    /// Gets the current board state from the game.
    /// </summary>
    public Board CurrentBoard
    {
        get
        {
            // Attempts to retrieve the current board from the board timeline.
            game.BoardTimeline.TryGetCurrent(out Board currentBoard);
            return currentBoard;
        }
    }

    /// <summary>
    /// Gets the side (White/Black) whose turn it is to move.
    /// </summary>
    public Side SideToMove
    {
        get
        {
            // Retrieves the current game conditions and returns the active side.
            game.ConditionsTimeline.TryGetCurrent(out GameConditions currentConditions);
            return currentConditions.SideToMove;
        }
        set
        {
            // Sets the side to move in the current game conditions.
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
            game.ConditionsTimeline[game.ConditionsTimeline.HeadIndex] = currentConditions;
        }
    }

    /// <summary>
    /// Gets the side that started the game.
    /// </summary>
    public Side StartingSide => game.ConditionsTimeline[0].SideToMove;

    /// <summary>
    /// Gets the timeline of half-moves made in the game.
    /// </summary>
    public Timeline<HalfMove> HalfMoveTimeline => game.HalfMoveTimeline;

    /// <summary>
    /// Gets the index of the most recent half-move.
    /// </summary>
    public int LatestHalfMoveIndex => game.HalfMoveTimeline.HeadIndex;

    /// <summary>
    /// Computes the full move number based on the starting side and the latest half-move index.
    /// </summary>
    public int FullMoveNumber => StartingSide switch
    {
        Side.White => LatestHalfMoveIndex / 2 + 1,
        Side.Black => (LatestHalfMoveIndex + 1) / 2 + 1,
        _ => -1
    };

    private bool isWhiteAI;
    private bool isBlackAI;

    /// <summary>
    /// Gets a list of all current pieces on the board, along with their positions.
    /// </summary>
    public List<(Square, Piece)> CurrentPieces
    {
        get
        {
            // Clear the backing list before populating with current pieces.
            currentPiecesBacking.Clear();
            // Iterate over every square on the board.
            for (int file = 1; file <= 8; file++)
            {
                for (int rank = 1; rank <= 8; rank++)
                {
                    Piece piece = CurrentBoard[file, rank];
                    // If a piece exists at this position, add it to the list.
                    if (piece != null) currentPiecesBacking.Add((new Square(file, rank), piece));
                }
            }
            return currentPiecesBacking;
        }
    }

    // Backing list for storing current pieces on the board.
    private readonly List<(Square, Piece)> currentPiecesBacking = new List<(Square, Piece)>();

    // Reference to the debug utility for the chess engine.
    [SerializeField] private UnityChessDebug unityChessDebug;
    // The current game instance.
    public Game game;
    // Serializers for game state (FEN and PGN formats).
    private FENSerializer fenSerializer;
    private PGNSerializer pgnSerializer;
    // Cancellation token source for asynchronous promotion UI tasks.
    public CancellationTokenSource promotionUITaskCancellationTokenSource;
    // Stores the user's choice for promotion; initialised to none.
    private ElectedPiece userPromotionChoice = ElectedPiece.None;
    // Mapping of game serialization types to their corresponding serializers.
    private Dictionary<GameSerializationType, IGameSerializer> serializersByType;
    // Currently selected serialization type (default is FEN).
    private GameSerializationType selectedSerializationType = GameSerializationType.FEN;



    // Used to store partial promotion moves while we wait for the client to pick a piece.
    private readonly Dictionary<Square, PromotionMove> pendingPromotionMoves = new Dictionary<Square, PromotionMove>();

    /// <summary>
    /// Unity's Start method initialises the game and sets up event handlers.
    /// </summary>
    public NetworkVariable<bool> gamehasended =  new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);





    public void Start()
    {
        // Subscribe to the event triggered when a visual piece is moved.
        VisualPiece.VisualPieceMoved += OnPieceMoved;

        // Initialise the serializers for FEN and PGN formats.
        serializersByType = new Dictionary<GameSerializationType, IGameSerializer>
        {
            [GameSerializationType.FEN] = new FENSerializer(),
            [GameSerializationType.PGN] = new PGNSerializer()
        };


        StartNewGame();

#if DEBUG_VIEW
		// Enable debug view if compiled with DEBUG_VIEW flag.
		unityChessDebug.gameObject.SetActive(true);
		unityChessDebug.enabled = true;
#endif
    }

    /// <summary>
    /// Starts a new game by creating a new game instance and invoking the NewGameStartedEvent.
    /// </summary>
    public async void StartNewGame()
    {
        gamehasended.Value = false;
        game = new Game();
        NewGameStartedEvent?.Invoke();
    }

    /// <summary>
    /// Serialises the current game state using the selected serialization format.
    /// </summary>
    /// <returns>A string representing the serialised game state.</returns>
      #region Simple Save/Load of entire game

    public string SerializeGame()
    {
        if (serializersByType.TryGetValue(selectedSerializationType, out IGameSerializer serializer))
        {
            return serializer.Serialize(game);
        }
        return null;
    }

    public void LoadGame(string serialized)
    {



        // Deserialize the FEN into a structured game state
        game = new FENSerializer().Deserialize(serialized);

        char turnIndicator = serialized.Split(' ')[1][0];
        Side activePlayer = (turnIndicator == 'w') ? Side.White : Side.Black;



        // Clear visual board but don't destroy pre-placed pieces
        BoardManager.Instance.ClearBoard();

        // Retrieve pieces from game state and move them
        foreach ((Square position, Piece piece) in GameManager.Instance.CurrentPieces)
        {
            BoardManager.Instance.CreateAndPlacePieceGO(piece, position);

        }

        //EndTurn();

        // Update UI
        //UIManager.Instance.UpdateGameStringInputField();
        NotifyTurnChangeClientRpc(isWhiteTurn);
        UpdateTurnIndicatorClientRpc(isWhiteTurn);
        UIManager.Instance.ValidateIndicators();
    }


    [ServerRpc(RequireOwnership = false)]
    public void SyncPieceNameServerRpc(ulong networkObjectId, string pieceName)
    {
        GameManager.Instance.gamehasended.Value = false;

        SyncPieceNameClientRpc(networkObjectId, pieceName);
    }

    [ClientRpc]
    private void SyncPieceNameClientRpc(ulong networkObjectId, string pieceName)
    {
        StartCoroutine(EnsureObjectAndRename(networkObjectId, pieceName));
    }

    // Coroutine ensures that the object exists before renaming
    private IEnumerator EnsureObjectAndRename(ulong networkObjectId, string pieceName)
    {
        GameObject pieceObject = null;

        // Wait until the object exists on the client
        while (pieceObject == null)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
            {
                pieceObject = netObj.gameObject;
            }
            yield return null; // Wait for the next frame
        }

        pieceObject.name = pieceName; // Assign correct name
    }


    /// <summary>
    /// Resets the game to a specific half-move index.
    /// </summary>
    /// <param name="halfMoveIndex">The target half-move index to reset the game to.</param>
    public void ResetGameToHalfMoveIndex(int halfMoveIndex)
    {
        // If the reset operation fails, exit early.
        if (!game.ResetGameToHalfMoveIndex(halfMoveIndex)) return;

        // Disable promotion UI and cancel any pending promotion tasks.
        UIManager.Instance.SetActivePromotionUI(false);
        promotionUITaskCancellationTokenSource?.Cancel();
        // Notify subscribers that the game has been reset to a half-move.
        GameResetToHalfMoveEvent?.Invoke();
    }

    /// <summary>
    /// Attempts to execute a given move in the game.
    /// </summary>
    /// <param name="move">The move to execute.</param>
    /// <returns>True if the move was successfully executed; otherwise, false.</returns>
    private bool TryExecuteMove(Movement move)
    {

       
        if (!game.TryExecuteMove(move)) return false;

        // Check if that caused checkmate or stalemate
        HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);


        GetNumLegalMovesForCurrentPosition();
    

            if (latestHalfMove.CausedCheckmate || latestHalfMove.CausedStalemate)
        {
            // Possibly broadcast game end
            bool isWhiteWin = latestHalfMove.CausedCheckmate && latestHalfMove.Piece.Owner == Side.White;
            if (DebugMode) Debug.Log("[GameManager] Game End condition met. Invoking GameEndedEvent.");


            DeclareGameEndServerRpc(latestHalfMove.CausedCheckmate, isWhiteWin: isWhiteWin);



        }
        else
        {
            BoardManager.Instance.EnsureOnlyPiecesOfSideAreEnabled(SideToMove);
        }

        MoveExecutedEvent?.Invoke();
        // Switch side to move
        SideToMove = (SideToMove == Side.White) ? Side.Black : Side.White;
        return true;
    }



    /// <summary>
    /// Blocks until the user selects a piece for pawn promotion.
    /// </summary>
    /// <returns>The elected promotion piece chosen by the user.</returns>
    public ElectedPiece GetUserPromotionPieceChoice()
    {
        // Wait until the user selects a promotion piece.
        while (userPromotionChoice == ElectedPiece.None) { }

        ElectedPiece result = userPromotionChoice;
        // Reset the user promotion choice.
        userPromotionChoice = ElectedPiece.None;
        return result;
    }

    /// <summary>
    /// Allows the user to elect a promotion piece.
    /// </summary>
    /// <param name="choice">The elected promotion piece.</param>
    public void ElectPiece(ElectedPiece choice)
    {
        userPromotionChoice = choice;
    }

    /// <summary>
    /// Handles the event triggered when a visual chess piece is moved.
    /// This method validates the move, handles special moves, and updates the board state.
    /// </summary>
    /// <param name="movedPieceInitialSquare">The original square of the moved piece.</param>
    /// <param name="movedPieceTransform">The transform of the moved piece.</param>
    /// <param name="closestBoardSquareTransform">The transform of the closest board square.</param>
    /// <param name="promotionPiece">Optional promotion piece (used in pawn promotion).</param>
    /// 
    [System.Serializable]
    public class MoveData
    {
        public string Square;
        public string StartSquare;
        public string EndSquare;
        public string PromotionPiece;
    }
    public static class PieceFactory
    {
        public static Piece CreatePiece(string pieceType)
        {
            return pieceType switch
            {
                "Pawn" => new Pawn(),
                "Knight" => new Knight(),
                "Bishop" => new Bishop(),
                "Rook" => new Rook(),
                "Queen" => new Queen(),
                "King" => new King(),
                _ => throw new ArgumentException("Invalid piece type")
            };
        }
    }

    public string ConvertGameStateToString()
    {
        return JsonUtility.ToJson(game); // to avoid complicated stupid network serialization >:((((
    }





    private void OnPieceMoved(Square startSquare,
                         Transform movedPieceTransform,
                         Transform droppedSquareTransform,
                         Piece promotionPiece)
    {
        MoveDTO moveDTO = new MoveDTO
        {
            initialSquare = new SerializableSquare(startSquare.File, startSquare.Rank),
            pieceTransformName = movedPieceTransform.name,
            destinationSquareTransformName = droppedSquareTransform.name,
            promotionPieceType = promotionPiece != null ? promotionPiece.GetType().Name : null
        };

        // Convert move data to JSON
        string moveJson = JsonUtility.ToJson(moveDTO);

        // Get the local client ID
        ulong clientId = NetworkManager.Singleton.LocalClientId;

        // Immediately request server validation
        RequestMoveValidationServerRpc(moveJson, clientId);
    }


    /// <summary>
    /// Determines whether the specified piece has any legal moves.
    /// </summary>
    /// <param name="piece">The chess piece to evaluate.</param>
    /// <returns>True if the piece has at least one legal move; otherwise, false.</returns>
    public bool HasLegalMoves(Piece piece)
    {

        return game.TryGetLegalMovesForPiece(piece, out _);
    }


    /////////  Server sided code starts here /////////


    private List<SerializableSquare> destroyedPieces = new List<SerializableSquare>();


    public bool isWhiteTurn = true;


    public ulong LocalClientId => NetworkManager.Singleton.LocalClientId;


    // Store connected player IDs
    public List<ulong> connectedPlayers = new List<ulong>();

    private NetworkVariable<float> clientPing = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            if (DebugMode) Debug.Log("GameManager initialized on the server.");
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            InvokeRepeating(nameof(SendPingRequest), 1f, 15f); // Ping every 15 seconds
            WhitePFP.Value = "";
            BlackPFP.Value = "";

        }
    }

    /// <summary>
    /// Sets the PFP for the correct player based on their slot.
    /// Player 0 is White, Player 1 is Black.
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

    // Send ping request from server to all clients (including host)
    private void SendPingRequest()
    {
        if (IsServer)
        {
            float timestamp = Time.time;
            RequestPingClientRpc(timestamp);
        }
    }


    [ClientRpc]
    private void RequestPingClientRpc(float timestamp, ClientRpcParams clientRpcParams = default)
    {
        if (IsClient)
        {
            RespondPingServerRpc(timestamp);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RespondPingServerRpc(float timestamp, ServerRpcParams serverRpcParams = default)
    {
        if (IsServer)
        {
            // Calculate round-trip time (RTT)
            float roundTripTime = (Time.time - timestamp) * 1000f; // Convert to ms
            ulong clientId = serverRpcParams.Receive.SenderClientId;

            //Debug.Log($"[SERVER] Ping from Client {clientId}: {roundTripTime:F1} ms");

            // Store the latest ping value
            clientPing.Value = roundTripTime;

            // Send the updated ping to both host and clients
            UpdatePingClientRpc(clientId, roundTripTime);
        }
    }

    // Send the updated ping to both client and host UI
    [ClientRpc]
    private void UpdatePingClientRpc(ulong clientId, float ping)
    {
        if (UIManager.Instance != null)
        {
            Debug.Log($"[CLIENT] Updated Ping for Client {clientId}: {ping:F1} ms");
        }
    }
    private void OnClientConnected(ulong clientId)
    {
        connectedPlayers.Add(clientId);
        DLCManager.Instance.userID = clientId.ToString();
        DLCManager.Instance.LoadUserSelectedPFP(); // Auto-load stored PFP

        if (DebugMode) Debug.Log($"Player {clientId} connected. Total Players: {connectedPlayers.Count}");
        LoadGame("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"); // Load the default game state for all players as they connect host is responsable for saving the game state by keeping track the string of the game state
                                                                              //which they can easily do by saving that  text in the host and load back clients
        UpdateConnectedPlayersClientRpc(connectedPlayers.ToArray());


        // If at least two players are connected, start the game
        if (IsServer && connectedPlayers.Count >= 2)
        {
            if (DebugMode) Debug.Log("Two players connected, starting the game...");
        }
    }

    [ClientRpc]
    private void UpdateConnectedPlayersClientRpc(ulong[] playerIds)
    {
        // Use the received array to update a local list on the client.
        connectedPlayers = new List<ulong>(playerIds);
        if (DebugMode) Debug.Log("Updated connected players on client.");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        connectedPlayers.Remove(clientId);
        if (DebugMode) Debug.Log($"[Server] Player {clientId} disconnected.");




        // Check if the host (server) disconnected
        if (clientId == NetworkManager.ServerClientId)
        {
            if (DebugMode) Debug.Log("Host disconnected! Resetting game.");
            ResetGame();

            // Optionally, notify all clients that the session has ended
            NotifyGameEndClientRpc(false, false);
        }
        else if (IsServer && connectedPlayers.Count == 0)
        {
            if (DebugMode) Debug.Log("All players disconnected. Resetting game.");
            ResetGame();
        }
    }


    private void ResetGame()
    {
        if (DebugMode) Debug.Log("[Server] Resetting game...");
        StartNewGame();
        destroyedPieces.Clear();
    }

    /// <summary>
    /// Return an ElectedPiece enum from the name "Queen", "Knight", etc.
    /// For simpler logic, you can also map those piece names directly.
    /// </summary>
    private ElectedPiece ConvertElectedPieceType(string pieceTypeName)
    {
        return pieceTypeName switch
        {
            "Queen" => ElectedPiece.Queen,
            "Rook" => ElectedPiece.Rook,
            "Bishop" => ElectedPiece.Bishop,
            "Knight" => ElectedPiece.Knight,
            _ => ElectedPiece.Queen // fallback
        };
    }


    /// <summary>
    /// Ends the current turn and updates all clients.
    /// </summary>
    public void EndTurn()
    {

        if (gamehasended.Value)
        {
            return;
        }

        isWhiteTurn = !isWhiteTurn;
            SideToMove = isWhiteTurn ? Side.White : Side.Black;


            NotifyTurnChangeClientRpc(isWhiteTurn);
            UpdateTurnIndicatorClientRpc(isWhiteTurn); // Force UI update on all clients
        
       
    }

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
    /// Notifies all clients about turn changes.
    /// </summary>
    [ClientRpc]
    private void NotifyTurnChangeClientRpc(bool whiteTurn)
    {
        if (gamehasended.Value)
        {
            return;
        }
        if (DebugMode) Debug.Log($"[CLIENT] Received Turn Change - Expected Turn: {(whiteTurn ? "White's Turn" : "Black's Turn")}");

        // Explicitly set the turn variable
        isWhiteTurn = whiteTurn;

        // Force update of SideToMove on the client
        SideToMove = isWhiteTurn ? Side.White : Side.Black;
        if (DebugMode) Debug.Log($"[CLIENT] Forced SideToMove Update - Now: {(SideToMove == Side.White ? "White" : "Black")}");

        // Update UI after ensuring SideToMove is set correctly
        UIManager.Instance.ValidateIndicators();
    }



    /// <summary>
    /// Declares game end and syncs with clients.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void DeclareGameEndServerRpc(bool isCheckmate, bool isWhiteWin)
    {

        NotifyGameEndClientRpc(isCheckmate, isWhiteWin);
    }

    /// <summary>
    /// Notifies clients about the game result.
    /// </summary>
    [ClientRpc]
    private void NotifyGameEndClientRpc(bool isCheckmate, bool isWhiteWin)
    {
        string resultMessage = isCheckmate ? (isWhiteWin ? "White Wins!" : "Black Wins!") : "Draw!";

        UIManager.Instance.UpdateBoardTurn(resultMessage);

        BoardManager.Instance.SetActiveAllPieces(false);
        GameEndedEvent?.Invoke();
    }




    [ServerRpc(RequireOwnership = false)]
    public    void RequestMoveValidationServerRpc(string moveJson, ulong clientid)
    {
        if (DebugMode) Debug.Log($"[SERVER] Received move JSON: {moveJson}");


        MoveDTO moveData = JsonUtility.FromJson<MoveDTO>(moveJson);
        Square startSquare = moveData.initialSquare.ToSquare();
        GameObject endObj = GameObject.Find(moveData.destinationSquareTransformName);
        if (!endObj)
        {
            if (DebugMode) Debug.LogWarning("[Server] Destination transform not found. Rejecting move.");
            RejectMoveClientRpc(moveJson);
            return;
        }
        Square endSquare = new Square(endObj.name);




         // Attempt to retrieve a valid move
        if (!game.TryGetLegalMove(startSquare, endSquare, out Movement move))
        {
            if (DebugMode) Debug.LogWarning("[Server] Move was invalid according to the chess logic. Rejecting...");
            RejectMoveClientRpc(moveJson);
            return;
        }

        PromotionMove promoMove = move as PromotionMove;
        // If the move is a PromotionMove, set the promotion piece if provided in the DTO
        if (move is PromotionMove promoMove_)
        {
            if (!string.IsNullOrEmpty(moveData.promotionPieceType))
            {
                // We have some piece type from the client
                Piece finalPiece = PromotionUtil.GeneratePromotionPiece(
                    ConvertElectedPieceType(moveData.promotionPieceType),
                    SideToMove
                );
                promoMove.SetPromotionPiece(finalPiece);
            }
            // else if no piece was provided, you might do something else
        }

        //int count = pendingPromotionMoves.Count;
        // Handle special moves
        if (promoMove is SpecialMove specialMove )
        {
            // We have a promotion move with no piece set -> we must ask the client
            if (DebugMode) Debug.Log($"[Server] Received PromotionMove for {startSquare}->{endSquare}, but no piece chosen yet.");

            // Store it so we can finalize later once the client picks
            pendingPromotionMoves[startSquare] = promoMove;

            ClientRpcParams targetSender = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientid } }
            };


            AskPromotionChoiceClientRpc(moveJson, targetSender);

            // Return here so we do NOT finalize the move yet
            return;


        }

        if (move is SpecialMove specialMove_ && !TryHandleSpecialMoveBehaviourServer(specialMove_))
        {
            if (DebugMode) Debug.Log("[Server] Special move handling failed. Rejecting...");
            RejectMoveClientRpc(moveJson);
            return;
        }

        // Attempt to execute the move within the chess logic
        if (!TryExecuteMove(move))
        {
            if (DebugMode) Debug.LogWarning("[Server] Move could not be executed. Rejecting...");
            RejectMoveClientRpc(moveJson);
            return;
        }

        // Move was successful, broadcast to all clients
        ValidateAndExecuteMoveClientRpc(moveJson);

    }


    /// <summary>
    /// “Server” version that handles castling/en passant/promotion visual logic and
    /// also modifies the shared 'destroyedPieces' if we capture something.
    /// </summary>
    /// </summary>
    private bool TryHandleSpecialMoveBehaviourServer(SpecialMove specialMove)
    {
        switch (specialMove)
        {
            case CastlingMove castlingMove:
                // Rook is effectively “moved” from its start square to rook-end
                return true;

            case EnPassantMove epMove:
                SerializableSquare capturedPawnSquare = SerializableSquare.FromSquare(epMove.CapturedPawnSquare) ;
                destroyedPieces.Add(capturedPawnSquare);
                return true;

            case PromotionMove promoMove:
                // If the piece is not yet assigned, the client must provide it (or we pick default).
                if (promoMove.PromotionPiece == null)
                {
                    // If still null, we can’t do the move yet. Could queue or reject.
                    return false;
                }
                // Otherwise, this is basically a normal move plus piece replacement.
                return true;

            default:
                return false;
        }
    }
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

    [ServerRpc(RequireOwnership = false)]
    private void DestroyPieceServerRpc(ulong networkObjectId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
        {
            netObj.Despawn();
        }
    }



    [ClientRpc]
    private void ValidateAndExecuteMoveClientRpc(string moveJson)
    {

        MoveDTO moveData = JsonUtility.FromJson<MoveDTO>(moveJson);

        // In singleplayer code, we’d do BoardManager logic directly here
        // But we must be careful: the server side already changed the “game” state,
        // so we just do the visuals.

        Square startSquare = moveData.initialSquare.ToSquare();

        Piece piece = CurrentBoard[startSquare];

        GameObject pieceObj = GameObject.Find(moveData.pieceTransformName);
        if (!pieceObj)
        {
            if (DebugMode) Debug.LogWarning("[Client] Could not find piece to move. Possibly out of sync?");
            return;
        }

        GameObject endObj = GameObject.Find(moveData.destinationSquareTransformName);
        if (!endObj)
        {
            if (DebugMode) Debug.LogWarning("[Client] Could not find end transform. Possibly out of sync?");
            return;
        }

        // If anything was on the end square, remove it (capture).
        Square endSquare = new Square(endObj.name);
        BoardManager.Instance.TryDestroyVisualPiece(endSquare);



        // If this was a promotion, the server side logic replaced the piece in “game”,
        // so we can reflect that visually:
        //   1) Destroy the original piece’s GO
        //   2) Create the new piece
        //   3) Move it

        // Check if we had a promotion piece
        if (!string.IsNullOrEmpty(moveData.promotionPieceType))
        {
            // Destroy old piece
            BoardManager.Instance.TryDestroyVisualPiece(startSquare);
            BoardManager.Instance.TryDestroyVisualPiece(endSquare);

            if (DebugMode) Debug.Log("spawning a piece!");
            // Rebuild the newly promoted piece
            Piece newPiece = PromotionUtil.GeneratePromotionPiece(
                ConvertElectedPieceType(moveData.promotionPieceType),
                SideToMove
            );
            BoardManager.Instance.CreateAndPlacePieceGO(newPiece, endSquare);
            GameObject newPieceGO = BoardManager.Instance.GetPieceGOAtPosition(endSquare);


            GameObject endSquareGO = GameObject.Find(moveData.destinationSquareTransformName);
            GameManager.Instance.CurrentBoard[endSquare] = newPiece;
            //if (endSquareGO != null && newPieceGO != null)
            //{
            //    newPieceGO.transform.SetParent(endSquareGO.transform, true);
            //}
            //else
            //{
            //    Debug.LogWarning("[Client] Could not find board square for promotion or new piece is null.");
            


            //BoardManager.Instance.TryDestroyVisualPiece(startSquare);  // old piece
            //BoardManager.Instance.CreateAndPlacePieceGO(piece, endSquare);
        }
        else
        {
            // Normal or Special Move: just re-parent the piece
            pieceObj.transform.SetParent(endObj.transform);
            pieceObj.transform.position = endObj.transform.position;
        }
        EndTurn();
        UIManager.Instance.ValidateIndicators();

        // Also update local UI, turn indicators, etc.
    }



 

    [ClientRpc] // start startSquare
    private void RejectMoveClientRpc(string JASON)
    {
        MoveDTO moveData = JsonUtility.FromJson<MoveDTO>(JASON);

        GameObject pieceObj = GameObject.Find(moveData.pieceTransformName);
        if (!pieceObj) return;

        // Snap piece back to its parent square’s position
        pieceObj.transform.position = pieceObj.transform.parent.position;
    }

    public void AddDestroyedPiece(Square position)
    {
        destroyedPieces.Add(new SerializableSquare(position.File, position.Rank));
    }



    [ServerRpc(RequireOwnership = false)]
    public void DestoryPieceServerRpc(ulong networkObjectId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
        {
            netObj.Despawn();
        }
    }

    [ClientRpc]
    private void AskPromotionChoiceClientRpc(string moveJson,  ClientRpcParams clientRpcParams = default)
    {
        UIManager.Instance.SetActivePromotionUI(true);
        BoardManager.Instance.SetActiveAllPieces(false);

        // Cancel any pending promotion UI tasks.
        promotionUITaskCancellationTokenSource?.Cancel();
        promotionUITaskCancellationTokenSource = new CancellationTokenSource();

        if (DebugMode) Debug.Log("[Client] The server wants us to pick a promotion piece!");

        HandlePromotionChoiceAsync(moveJson);
    }


    private async void HandlePromotionChoiceAsync(string moveJson)
    {
        if (DebugMode) Debug.Log("[Client] The server wants us to pick a promotion piece!");

        // Await the asynchronous user input (e.g., promotion piece selection).
        ElectedPiece choice = await Task.Run(GetUserPromotionPieceChoice, promotionUITaskCancellationTokenSource.Token);

        // Deactivate the promotion UI and re-enable all pieces.
        UIManager.Instance.SetActivePromotionUI(false);
        BoardManager.Instance.SetActiveAllPieces(true);

        string chosenPieceType = choice.ToString();
        SubmitPromotionChoiceServerRpc(moveJson, chosenPieceType);
    }


    [ServerRpc(RequireOwnership = false)]
    public void SubmitPromotionChoiceServerRpc(string moveJson, string chosenPieceType , ServerRpcParams rpcParams = default)
    {
        // 1) Parse the same data
        MoveDTO moveData = JsonUtility.FromJson<MoveDTO>(moveJson);
        Square startSquare = moveData.initialSquare.ToSquare();


        // 2) Retrieve our partial PromotionMove from the dictionary
        if (!pendingPromotionMoves.TryGetValue(startSquare, out PromotionMove promoMove))
        {
            if (DebugMode) Debug.LogWarning("[Server] No pending promotion move found for " + startSquare);
            RejectMoveClientRpc(moveJson);
            return;
        }

        // 3) Set the piece now that the user has chosen
        Piece finalPiece = PromotionUtil.GeneratePromotionPiece(
            ConvertElectedPieceType(chosenPieceType),
            SideToMove
        );
        promoMove.SetPromotionPiece(finalPiece);


        // Done with the pending entry
        pendingPromotionMoves.Remove(startSquare);

        // 4) Now handle special moves if needed
        if (!TryHandleSpecialMoveBehaviourServer(promoMove))
        {
            if (DebugMode) Debug.LogWarning("[Server] special move handling failed for promotion. Rejecting...");
            RejectMoveClientRpc(moveJson);
            return;
        }

        // 5) Attempt to finalize
        if (!TryExecuteMove(promoMove))
        {
            if (DebugMode) Debug.LogWarning("[Server] Move could not be executed. Rejecting...");
            RejectMoveClientRpc(moveJson);
            return;
        }

        // 6) Because it’s a final promotion, we must update the “moveJson” so that “promotionPieceType”
        // is no longer empty. Then we pass that back to the clients for visuals.
        moveData.promotionPieceType = chosenPieceType;
        string finalJson = JsonUtility.ToJson(moveData);

        // 7) Let everyone do the final visuals
        ValidateAndExecuteMoveClientRpc(finalJson);
        //string serializedGameState = ConvertGameStateToString();
        //UpdateGameStateClientRpc(serializedGameState);

    }

    [ClientRpc]
    public void UpdateGameStateClientRpc(string serializedGameState)
    {
        GameManager.Instance.LoadGame(serializedGameState);
        UIManager.Instance.UpdateGameStringInputField();
    }

    [ClientRpc]
    public void RequestSetParentClientRpc(ulong childId, ulong parentId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(childId, out NetworkObject childNetObj) &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(parentId, out NetworkObject parentNetObj))
        {
            // Set the parent manually on the client
            childNetObj.transform.SetParent(parentNetObj.transform, true);
            if (DebugMode) if (DebugMode) Debug.Log($"[Client] Successfully set parent of {childNetObj.gameObject.name} to {parentNetObj.gameObject.name}");
        }
        else
        {
            if (DebugMode) if (DebugMode) Debug.LogWarning("[Client] Could not set parent! Objects not found.");
        }
    }
    public int GetNumLegalMovesForCurrentPosition()
    {
        // Retrieve the current board and game conditions
        game.BoardTimeline.TryGetCurrent(out Board currentBoard);
        game.ConditionsTimeline.TryGetCurrent(out GameConditions currentConditions);

        // Calculate the legal moves for the current position
        var legalMovesByPiece = Game.CalculateLegalMovesForPosition(currentBoard, currentConditions);

        if (DebugMode) Debug.Log("[DEBUG] Listing legal moves for the king:");

        int kingMoveCount = 0;
        foreach (var pieceMoves in legalMovesByPiece)
        {
            Piece piece = pieceMoves.Key;

            // Only check the king's moves
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








#endregion