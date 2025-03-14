using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityChess;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using Unity.Netcode.Components;


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


    // Events signalling various game state changes.
    public static event Action NewGameStartedEvent;
    public static event Action GameEndedEvent;
    public static event Action GameResetToHalfMoveEvent;
    
    public static event Action MoveExecutedEvent;



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

    /// <summary>
    /// Unity's Start method initialises the game and sets up event handlers.
    /// </summary>

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
        if (!string.IsNullOrEmpty(serialized) &&
            serializersByType.TryGetValue(selectedSerializationType, out IGameSerializer serializer))
        {
            game = serializer.Deserialize(serialized);
            NewGameStartedEvent?.Invoke();
        }
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
        if (latestHalfMove.CausedCheckmate || latestHalfMove.CausedStalemate)
        {
            // Possibly broadcast game end
            GameEndedEvent?.Invoke();
        }

        MoveExecutedEvent?.Invoke();
        // Switch side to move
        SideToMove = (SideToMove == Side.White) ? Side.Black : Side.White;
        return true;
    }


    /// <summary>
    /// Handles special move behaviour asynchronously (castling, en passant, and promotion).
    /// </summary>
    /// <param name="specialMove">The special move to process.</param>
    /// <returns>A task that resolves to true if the special move was handled; otherwise, false.</returns>
    public async Task<bool> TryHandleSpecialMoveBehaviourAsync(SpecialMove specialMove)
    {
        switch (specialMove)
        {
            // Handle castling move.
            case CastlingMove castlingMove:
                BoardManager.Instance.CastleRook(castlingMove.RookSquare, castlingMove.GetRookEndSquare());
                return true;
            // Handle en passant move.
            case EnPassantMove enPassantMove:
                BoardManager.Instance.TryDestroyVisualPiece(enPassantMove.CapturedPawnSquare);
                return true;
            // Handle promotion move when no promotion piece has been selected yet.
            case PromotionMove { PromotionPiece: null } promotionMove:
                // Activate the promotion UI and disable all pieces.
                UIManager.Instance.SetActivePromotionUI(true);
                BoardManager.Instance.SetActiveAllPieces(false);

                // Cancel any pending promotion UI tasks.
                promotionUITaskCancellationTokenSource?.Cancel();
                promotionUITaskCancellationTokenSource = new CancellationTokenSource();

                // Await user's promotion choice asynchronously.
                ElectedPiece choice = await Task.Run(GetUserPromotionPieceChoice, promotionUITaskCancellationTokenSource.Token);

                // Deactivate the promotion UI and re-enable all pieces.
                UIManager.Instance.SetActivePromotionUI(false);
                BoardManager.Instance.SetActiveAllPieces(true);

                // If the task was cancelled, return false.
                if (promotionUITaskCancellationTokenSource == null
                    || promotionUITaskCancellationTokenSource.Token.IsCancellationRequested
                ) { return false; }

                // Set the chosen promotion piece.
                promotionMove.SetPromotionPiece(
                    PromotionUtil.GeneratePromotionPiece(choice, SideToMove)
                );
                // Update the board visuals for the promotion.
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.Start);
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.End);
                BoardManager.Instance.CreateAndPlacePieceGO(promotionMove.PromotionPiece, promotionMove.End);

                promotionUITaskCancellationTokenSource = null;
                return true;
            // Handle promotion move when the promotion piece is already set.
            case PromotionMove promotionMove:
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.Start);
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.End);
                BoardManager.Instance.CreateAndPlacePieceGO(promotionMove.PromotionPiece, promotionMove.End);

                return true;
            // Default case: if the special move is not recognised.
            default:
                return false;
        }
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

    public void LoadGameFromString(string json)
    {
        game = JsonUtility.FromJson<Game>(json);
        NewGameStartedEvent?.Invoke();
    }


    private void OnPieceMoved(Square startSquare,
                         Transform movedPieceTransform,
                         Transform droppedSquareTransform,
                         Piece promotionPiece )
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


        // Immediately request server validation
        RequestMoveValidationServerRpc(moveJson);
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

    private bool gameStarted = false;

    public bool isWhiteTurn = true;


    public ulong LocalClientId => NetworkManager.Singleton.LocalClientId;


    // Store connected player IDs
    public List<ulong> connectedPlayers = new List<ulong>();

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Debug.Log("GameManager initialized on the server.");
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }



    private void OnClientConnected(ulong clientId)
    {
        connectedPlayers.Add(clientId);
        Debug.Log($"Player {clientId} connected. Total Players: {connectedPlayers.Count}");
        UpdateConnectedPlayersClientRpc(connectedPlayers.ToArray());
        NotifyTurnChangeClientRpc(isWhiteTurn);
        //Send current destroyed pieces to the reconnecting client
        SyncDestroyedPiecesClientRpc(clientId, destroyedPieces.ToArray());

        // Send current board state to ensure everything is synced
        SyncBoardStateClientRpc(clientId);

        // If at least two players are connected, start the game
        if (IsServer && connectedPlayers.Count >= 2)
        {
            Debug.Log("Two players connected, starting the game...");
            gameStarted = true;
        }
    }

    [ClientRpc]
    private void UpdateConnectedPlayersClientRpc(ulong[] playerIds)
    {
        // Use the received array to update a local list on the client.
        connectedPlayers = new List<ulong>(playerIds);
        Debug.Log("Updated connected players on client.");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        connectedPlayers.Remove(clientId);
        Debug.Log($"[Server] Player {clientId} disconnected.");




        // Check if the host (server) disconnected
        if (clientId == NetworkManager.ServerClientId)
        {
            Debug.Log("Host disconnected! Resetting game.");
            ResetGame();

            // Optionally, notify all clients that the session has ended
            NotifyGameEndClientRpc(false, false);
        }
        else if (IsServer && connectedPlayers.Count == 0)
        {
            Debug.Log("All players disconnected. Resetting game.");
            ResetGame();
        }
    }

    [ClientRpc]
    private void SyncDestroyedPiecesClientRpc(ulong clientId, SerializableSquare[] destroyedPiecesArray)
    {
        Debug.Log($"[CLIENT] Syncing destroyed pieces for reconnecting player {clientId}");

        foreach (SerializableSquare wrapper in destroyedPiecesArray)
        {
            //Square square = wrapper.ToSquare();
            BoardManager.Instance.TryDestroyVisualPiece(wrapper.ToSquare());
        }
    }

    [ClientRpc]
    private void SyncBoardStateClientRpc(ulong clientId)
    {
        Debug.Log($"[CLIENT] Syncing full board state for player {clientId}");

        foreach ((Square position, Piece piece) in GameManager.Instance.CurrentPieces)
        {
            GameObject pieceGO = BoardManager.Instance.GetPieceGOAtPosition(position);
            if (pieceGO == null)
            {
                // Ensure missing pieces are re-created for the reconnecting client
                BoardManager.Instance.CreateAndPlacePieceGO(piece, position);
            }
        }
    }


    private void ResetGame()
    {
        Debug.Log("[Server] Resetting game...");
        StartNewGame();
        destroyedPieces.Clear();
        gameStarted = false;
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
        isWhiteTurn = !isWhiteTurn;
        SideToMove = isWhiteTurn ? Side.White : Side.Black;


        NotifyTurnChangeClientRpc(isWhiteTurn);
        UpdateTurnIndicatorClientRpc(isWhiteTurn); // Force UI update on all clients
    }

    [ClientRpc]
    private void UpdateTurnIndicatorClientRpc(bool whiteTurn)
    {
        UIManager.Instance.ValidateIndicators();
    }

    /// <summary>
    /// Notifies all clients about turn changes.
    /// </summary>
    [ClientRpc]
    private void NotifyTurnChangeClientRpc(bool whiteTurn)
    {
        Debug.Log($"[CLIENT] Received Turn Change - Expected Turn: {(whiteTurn ? "White's Turn" : "Black's Turn")}");

        // Explicitly set the turn variable
        isWhiteTurn = whiteTurn;

        // Force update of SideToMove on the client
        SideToMove = isWhiteTurn ? Side.White : Side.Black;
        Debug.Log($"[CLIENT] Forced SideToMove Update - Now: {(SideToMove == Side.White ? "White" : "Black")}");

        // Update UI after ensuring SideToMove is set correctly
        UIManager.Instance.ValidateIndicators();
    }



    /// <summary>
    /// Declares game end and syncs with clients.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void DeclareGameEndServerRpc(bool isCheckmate, bool isWhiteWin)
    {
        if (!IsServer) return;
        NotifyGameEndClientRpc(isCheckmate, isWhiteWin);
    }

    /// <summary>
    /// Notifies clients about the game result.
    /// </summary>
    [ClientRpc]
    private void NotifyGameEndClientRpc(bool isCheckmate, bool isWhiteWin)
    {
        string resultMessage = isCheckmate ? (isWhiteWin ? "White Wins!" : "Black Wins!") : "Draw!";
        Debug.Log($"Game Over: {resultMessage}");
    }




    [ServerRpc(RequireOwnership = false)]
    public    void RequestMoveValidationServerRpc(string moveJson)
    {
        Debug.Log($"[SERVER] Received move JSON: {moveJson}");


        MoveDTO moveData = JsonUtility.FromJson<MoveDTO>(moveJson);
        Square startSquare = moveData.initialSquare.ToSquare();
        GameObject endObj = GameObject.Find(moveData.destinationSquareTransformName);
        if (!endObj)
        {
            Debug.LogWarning("[Server] Destination transform not found. Rejecting move.");
            RejectMoveClientRpc(moveJson);
            return;
        }
        Square endSquare = new Square(endObj.name);




         // Attempt to retrieve a valid move
        if (!game.TryGetLegalMove(startSquare, endSquare, out Movement move))
        {
            Debug.LogWarning("[Server] Move was invalid according to the chess logic. Rejecting...");
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
        // Handle special moves
        if (promoMove is SpecialMove specialMove)
        {

            // We'll do the special move logic here on the server
            if (!TryHandleSpecialMoveBehaviourServer(specialMove))
            {
                Debug.LogWarning("[Server] Special Move handling failed. Rejecting...");
                RejectMoveClientRpc(moveJson);
                return;
            }
        }

        // Attempt to execute the move within the chess logic
        if (!TryExecuteMove(move))
        {
            Debug.LogWarning("[Server] Move could not be executed. Rejecting...");
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



    [ClientRpc]
    private void ValidateAndExecuteMoveClientRpc(string moveJson)
    {
        MoveDTO moveData = JsonUtility.FromJson<MoveDTO>(moveJson);

        // In singleplayer code, we’d do BoardManager logic directly here
        // But we must be careful: the server side already changed the “game” state,
        // so we just do the visuals.

        GameObject pieceObj = GameObject.Find(moveData.pieceTransformName);
        if (!pieceObj)
        {
            Debug.LogWarning("[Client] Could not find piece to move. Possibly out of sync?");
            return;
        }

        GameObject endObj = GameObject.Find(moveData.destinationSquareTransformName);
        if (!endObj)
        {
            Debug.LogWarning("[Client] Could not find end transform. Possibly out of sync?");
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
            BoardManager.Instance.TryDestroyVisualPiece(new Square(pieceObj.transform.parent.name));
            // Rebuild the newly promoted piece
            Piece newPiece = PromotionUtil.GeneratePromotionPiece(
                ConvertElectedPieceType(moveData.promotionPieceType),
                SideToMove
            );
            BoardManager.Instance.CreateAndPlacePieceGO(newPiece, endSquare);
        }
        else
        {
            // Normal or Special Move: just re-parent the piece
            pieceObj.transform.SetParent(endObj.transform);
            pieceObj.transform.position = endObj.transform.position;
        }
        EndTurn();
        // Also update local UI, turn indicators, etc.
        UIManager.Instance.ValidateIndicators();
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

    //[ClientRpc]
    //private void HandleSpecialMoveClientRpc(SerializablePiece piece, PromotionMove promotionMove)
    //{
    //    promotionMove.SetPromotionPiece(piece.ToPiece());
    //    EndTurn();
    //}


    [ServerRpc(RequireOwnership = false)]
    public void DestoryPieceServerRpc(ulong networkObjectId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
        {
            netObj.Despawn();
        }
    }




}








#endregion