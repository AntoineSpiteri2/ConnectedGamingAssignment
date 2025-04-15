using UnityEngine;                           // Provides core Unity classes and functionality.
using System;                                // Provides basic .NET system classes.
using System.Collections.Generic;            // Provides collection types like Dictionary and List.
using Unity.Netcode;                         // Provides networking functionality via Unity Netcode.
using UnityChess;                            // Contains chess-specific types (e.g., Piece, Square, etc.).
using static UnityChess.SquareUtil;          // Allows static access to helper methods in SquareUtil.

/// <summary>
/// Manages the visual representation of the chess board and piece placement.
/// Inherits from MonoBehaviourSingleton to ensure only one instance exists.
/// </summary>
public class BoardManager : MonoBehaviourSingleton<BoardManager>
{
    // An array holding references to all square GameObjects on the board (64 squares for an 8x8 board).
    private readonly GameObject[] allSquaresGO;

    // A dictionary mapping each chess Square to its corresponding GameObject.
    private Dictionary<Square, GameObject> positionMap;

    // Constant for the side length of the board plane (measured from the centre of one corner square to the centre of the opposite corner square).
    private const float BoardPlaneSideLength = 14f;

    // Half of the board plane's side length, for convenience in positioning calculations.
    private const float BoardPlaneSideHalfLength = BoardPlaneSideLength * 0.5f;

    // Vertical offset for the board (defines the board's height above the base).
    private const float BoardHeight = 1.6f;

    /// <summary>
    /// Constructor to initialize readonly fields.
    /// </summary>
    public BoardManager()
    {
        // Instantiate the array for storing all 64 square GameObjects.
        allSquaresGO = new GameObject[64];
    }

    /// <summary>
    /// Awake is called when the script instance is being loaded.
    /// Sets up the board by subscribing to game events and creating the square GameObjects.
    /// </summary>
    private void Awake()
    {
        // Initialize the dictionary mapping squares to their GameObjects.
        positionMap = new Dictionary<Square, GameObject>();

        // Find all board squares already placed in the scene (tagged with "Square").
        GameObject[] preplacedSquares = GameObject.FindGameObjectsWithTag("Square");

        // Loop through each found square GameObject.
        for (int i = 0; i < preplacedSquares.Length; i++)
        {
            GameObject squareGO = preplacedSquares[i];

            // Convert the GameObject's name into a Square object using a helper function from SquareUtil.
            Square square = StringToSquare(squareGO.name);

            // Add the square to the dictionary if it is not already present.
            if (!positionMap.ContainsKey(square))
            {
                positionMap.Add(square, squareGO);
            }

            // Store each square GameObject in the array for later use.
            allSquaresGO[i] = squareGO;
        }
    }

    /// <summary>
    /// Handles the castling of a rook by moving it from its original position to its new position.
    /// </summary>
    /// <param name="rookPosition">The starting square of the rook.</param>
    /// <param name="endSquare">The destination square for the rook.</param>
    public void CastleRook(Square rookPosition, Square endSquare)
    {
        // Retrieve the rook GameObject at the given starting square.
        GameObject rookGO = GetPieceGOAtPosition(rookPosition);
        // Set the rook's parent transform to the destination square's GameObject.
        rookGO.transform.parent = GetSquareGOByPosition(endSquare).transform;
        // Reset the local position so that the rook is correctly centered on the square.
        rookGO.transform.localPosition = Vector3.zero;
    }

    /// <summary>
    /// Instantiates and places the visual representation of a chess piece on the board.
    /// </summary>
    /// <param name="piece">The chess piece to display.</param>
    /// <param name="position">The board square where the piece should be placed.</param>
    public void CreateAndPlacePieceGO(Piece piece, Square position)
    {
        // Ensure this code runs on the server/host.
        if (!NetworkManager.Singleton.IsServer)
        {
            return;
        }

        // Get the piece type (e.g., "Pawn", "Knight", "Bishop") from the piece's class name.
        string pieceType = piece.GetType().Name;
        // Get the owner (e.g., "White" or "Black") from the piece's Owner property.
        string pieceColor = piece.Owner.ToString();
        // Construct the model name string for resource loading, e.g., "White Pawn".
        string modelName = $"{pieceColor} {pieceType}";
        // Build the path to the prefab resource.
        string path = "PieceSets/Marble/" + modelName;

        // Load the corresponding prefab from the Resources folder.
        GameObject pieceObject = Resources.Load("PieceSets/Marble/" + modelName) as GameObject;
        // Generate a unique name for the piece based on its color, type, and board position.
        pieceObject.name = $"{pieceColor}_{pieceType}_{position.File}{position.Rank}";
        string pieceID = pieceObject.name;

        // Instantiate the piece as a child of the square GameObject where it should be placed.
        GameObject pieceGO = Instantiate(pieceObject, positionMap[position].transform);
        pieceGO.name = pieceID;

        // Retrieve the NetworkObject component for networking.
        NetworkObject netObj = pieceGO.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            // Spawn the object so that it is replicated to all connected clients.
            netObj.Spawn();
            // Sync the piece's name across the network using a server RPC.
            GameManager.Instance.SyncPieceNameServerRpc(netObj.NetworkObjectId, pieceID);

            // Retrieve the destination square's NetworkObject component.
            GameObject parentGO = GetSquareGOByPosition(position);
            NetworkObject parentNetObj = parentGO.GetComponent<NetworkObject>();

            if (parentNetObj != null)
            {
                // Set the network parent of the piece for hierarchical networking.
                netObj.TrySetParent(parentNetObj, true); // The second argument indicates whether to preserve world space position.
                // Request client RPC to set the parent on all clients (workaround due to Unity Netcode limitations).
                GameManager.Instance.RequestSetParentClientRpc(netObj.NetworkObjectId, parentNetObj.NetworkObjectId);
            }
            else
            {
                Debug.LogWarning("Parent GameObject does not have a NetworkObject component!");
            }
        }
        else
        {
            Debug.LogWarning("Spawned piece prefab has no NetworkObject component!");
        }
    }

    /// <summary>
    /// Retrieves all square GameObjects within a specified radius of a given world-space position.
    /// </summary>
    /// <param name="squareGOs">List to be populated with the found square GameObjects.</param>
    /// <param name="positionWS">The world-space position to check around.</param>
    /// <param name="radius">The radius within which to search.</param>
    public void GetSquareGOsWithinRadius(List<GameObject> squareGOs, Vector3 positionWS, float radius)
    {
        // Compute the square of the radius for a more efficient comparison.
        float radiusSqr = radius * radius;
        // Loop through each square GameObject.
        foreach (GameObject squareGO in allSquaresGO)
        {
            // If the square's position is within the specified radius of the given world-space position, add it to the list.
            if ((squareGO.transform.position - positionWS).sqrMagnitude < radiusSqr)
                squareGOs.Add(squareGO);
        }
    }

    /// <summary>
    /// Sets the active state (enabled/disabled) of all visual pieces on the board.
    /// </summary>
    /// <param name="active">True to enable all pieces; false to disable them.</param>
    public void SetActiveAllPieces(bool active)
    {
        // Retrieve all VisualPiece components from child objects (include inactive ones).
        VisualPiece[] visualPieces = GetComponentsInChildren<VisualPiece>(true);
        foreach (VisualPiece pieceBehaviour in visualPieces)
            pieceBehaviour.enabled = active;
    }

    /// <summary>
    /// Enables only the pieces belonging to the specified side that also have legal moves.
    /// </summary>
    /// <param name="side">The side (White or Black) whose pieces are to be enabled.</param>
    public void EnsureOnlyPiecesOfSideAreEnabled(Side side)
    {
        // Retrieve all VisualPiece components from child objects (include inactive ones).
        VisualPiece[] visualPieces = GetComponentsInChildren<VisualPiece>(true);
        foreach (VisualPiece pieceBehaviour in visualPieces)
        {
            // Retrieve the corresponding chess piece from the current board based on its current square.
            Piece piece = GameManager.Instance.CurrentBoard[pieceBehaviour.CurrentSquare];
            // Enable the VisualPiece only if it belongs to the specified side and has legal moves.
            pieceBehaviour.enabled = pieceBehaviour.PieceColor == side && GameManager.Instance.HasLegalMoves(piece);
        }
    }

    /// <summary>
    /// Attempts to destroy the visual representation of a piece at a specified board square.
    /// </summary>
    /// <param name="position">The board square from which to remove the piece.</param>
    public void TryDestroyVisualPiece(Square position)
    {
        // This code only runs on the server.
        if (NetworkManager.Singleton.IsServer)
        {
            // Retrieve the VisualPiece component from the square's GameObject.
            VisualPiece visualPiece = positionMap[position].GetComponentInChildren<VisualPiece>();
            if (visualPiece != null)
            {
                // Try to get the NetworkObject component and check if the piece is spawned.
                NetworkObject networkObject = visualPiece.GetComponent<NetworkObject>();
                if (networkObject != null && networkObject.IsSpawned)
                {
                    // Store the network object ID for reference.
                    ulong objectId = networkObject.NetworkObjectId;
                    // Use a server RPC to destroy the piece on all clients.
                    GameManager.Instance.DestoryPieceServerRpc(objectId);
                }
            }
        }
    }

    /// <summary>
    /// Retrieves the GameObject representing the piece at a given board square.
    /// </summary>
    /// <param name="position">The board square to inspect.</param>
    /// <returns>The piece GameObject if one exists; otherwise, null.</returns>
    public GameObject GetPieceGOAtPosition(Square position)
    {
        // Get the square's GameObject corresponding to the provided board position.
        GameObject square = GetSquareGOByPosition(position);
        // If the square has no children, return null; otherwise, return the first child (representing the piece).
        return square.transform.childCount == 0 ? null : square.transform.GetChild(0).gameObject;
    }

    /// <summary>
    /// Computes the world-space offset for a given file or rank index.
    /// </summary>
    /// <param name="index">The file or rank index (typically 1 to 8).</param>
    /// <returns>The computed offset from the centre of the board plane.</returns>
    private static float FileOrRankToSidePosition(int index)
    {
        // Normalize the index between 0 and 1.
        float t = (index - 1) / 7f;
        // Linearly interpolate between negative and positive half the board's side length.
        return Mathf.Lerp(-BoardPlaneSideHalfLength, BoardPlaneSideHalfLength, t);
    }

    /// <summary>
    /// Clears all visual pieces from the board.
    /// </summary>
    public void ClearBoard()
    {
        // Get all VisualPiece components from the board (including inactive ones).
        VisualPiece[] pieces = GetComponentsInChildren<VisualPiece>(true);
        foreach (VisualPiece visualPiece in pieces)
        {
            // Check if the visual piece has a NetworkObject component.
            if (visualPiece.TryGetComponent(out NetworkObject netObj))
            {
                // Only the server should handle despawning network objects.
                if (NetworkManager.Singleton.IsServer)
                {
                    if (netObj.IsSpawned)
                    {
                        // Despawn the piece from the network and destroy it.
                        netObj.Despawn(true);
                    }
                    else
                    {
                        if (GameManager.Instance.DebugMode)
                            Debug.LogWarning($"[ClearBoard] Tried to despawn {visualPiece.name}, but it's not spawned.");
                    }
                }
                else
                {
                    // If not on the server, simply destroy the visual piece GameObject.
                    Destroy(visualPiece.gameObject);
                }
            }
            else
            {
                // If there is no NetworkObject, destroy the visual piece GameObject.
                Destroy(visualPiece.gameObject);
            }
        }
    }

    /// <summary>
    /// Retrieves the GameObject for a board square based on its chess notation.
    /// </summary>
    /// <param name="position">The board square to locate.</param>
    /// <returns>The corresponding square GameObject.</returns>
    public GameObject GetSquareGOByPosition(Square position) =>
        // Uses Array.Find to search within the allSquaresGO array for a GameObject whose name matches the square's string representation.
        Array.Find(allSquaresGO, go => go.name == SquareToString(position));
}
