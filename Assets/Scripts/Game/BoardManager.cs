using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityChess;
using UnityEngine;
using static UnityChess.SquareUtil;

/// <summary>
/// Manages the visual representation of the chess board and piece placement.
/// Inherits from MonoBehaviourSingleton to ensure only one instance exists.
/// </summary>
public class BoardManager : MonoBehaviourSingleton<BoardManager>
{

    // Array holding references to all square GameObjects (64 squares for an 8x8 board).
    private readonly GameObject[] allSquaresGO;
    // Dictionary mapping board squares to their corresponding GameObjects.
    private Dictionary<Square, GameObject> positionMap;
    // Constant representing the side length of the board plane (from centre to centre of corner squares).
    private const float BoardPlaneSideLength = 14f; // measured from corner square centre to corner square centre, on same side.
                                                    // Half the side length, for convenience.
    private const float BoardPlaneSideHalfLength = BoardPlaneSideLength * 0.5f;
    // The vertical offset for placing the board (height above the base).
    private const float BoardHeight = 1.6f;

    /// <summary>
    /// Constructor to initialize readonly fields.
    /// </summary>
    public BoardManager()
    {
        allSquaresGO = new GameObject[64];
    }

    /// <summary>
    /// Awake is called when the script instance is being loaded.
    /// Sets up the board, subscribes to game events, and creates the square GameObjects.
    /// </summary>
    private void Awake()
    {
        // Ensure this instance is assigned.

        // Initialize the dictionary.
        positionMap = new Dictionary<Square, GameObject>();

        // Find all board squares that are pre-placed in the scene.
        GameObject[] preplacedSquares = GameObject.FindGameObjectsWithTag("Square");

        // Optionally, initialize the allSquaresGO array based on the number of found squares.
        for (int i = 0; i < preplacedSquares.Length; i++)
        {
            GameObject squareGO = preplacedSquares[i];

            // Option: Validate the square's name to ensure it follows your chess notation
            // For example, "a1", "b2", etc. You might have a helper method to convert that.
            Square square = StringToSquare(squareGO.name);

            // Add the square to the dictionary.
            if (!positionMap.ContainsKey(square))
            {
                positionMap.Add(square, squareGO);
            }

            // Also store it in the array.
            allSquaresGO[i] = squareGO;
        }
    }


  
    /// <summary>
    /// Handles the castling of a rook.
    /// Moves the rook from its original position to its new position.
    /// </summary>
    /// <param name="rookPosition">The starting square of the rook.</param>
    /// <param name="endSquare">The destination square for the rook.</param>
    public void CastleRook(Square rookPosition, Square endSquare)
    {
        // Retrieve the rook's GameObject.
        GameObject rookGO = GetPieceGOAtPosition(rookPosition);
        // Set the rook's parent to the destination square's GameObject.
        rookGO.transform.parent = GetSquareGOByPosition(endSquare).transform;
        // Reset the local position so that the rook is centred on the square.
        rookGO.transform.localPosition = Vector3.zero;
    }

    /// <summary>
    /// Instantiates and places the visual representation of a piece on the board.
    /// </summary>
    /// <param name="piece">The chess piece to display.</param>
    /// <param name="position">The board square where the piece should be placed.</param>
    public void CreateAndPlacePieceGO(Piece piece, Square position)
    {
        // Ensure this code runs on the server/host  
        if (!NetworkManager.Singleton.IsServer)
        {
            return;
        }

        string pieceType = piece.GetType().Name;  // Should return "Pawn", "Knight", "Bishop", etc.
        string pieceColor = piece.Owner.ToString(); // Should return "White" or "Black"
        string modelName = $"{pieceColor} {pieceType}";  // Example: "White Pawn"
        string path = "PieceSets/Marble/" + modelName;

        //Debug.Log($"Spawning piece: {pieceColor} {pieceType} at {position} using path: {path}");

        GameObject pieceObject = Resources.Load("PieceSets/Marble/" + modelName) as GameObject;
        pieceObject.name = $"{pieceColor}_{pieceType}_{position.File}{position.Rank}"; // generate a unique name for the piece to prevent accidently moving other pieces and quick identification
        string pieceID = pieceObject.name;

        GameObject pieceGO = Instantiate(pieceObject, positionMap[position].transform);
        pieceGO.name = pieceID;

        NetworkObject netObj = pieceGO.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn(); // Replicates the object to all clients  very stright forward indeed
            GameManager.Instance.SyncPieceNameServerRpc(netObj.NetworkObjectId, pieceID); // Ensure all clients get the correct name cause funny unity netcode didn't do it for us thanks devs


            // Retrieve the parent's NetworkObject component  
            GameObject parentGO = GetSquareGOByPosition(position);
            NetworkObject parentNetObj = parentGO.GetComponent<NetworkObject>();

            if (parentNetObj != null)
            {
                // Correctly set the network parent  
                netObj.TrySetParent(parentNetObj, true); // Set to false if you want to change local position  
                GameManager.Instance.RequestSetParentClientRpc(netObj.NetworkObjectId, parentNetObj.NetworkObjectId); // thanks unitynet for not doing this for us  cause we need to do it manually
                //force client to set parent cause unity netcode is bad and didn't think this is important
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
    /// Retrieves all square GameObjects within a specified radius of a world-space position.
    /// </summary>
    /// <param name="squareGOs">A list to be populated with the found square GameObjects.</param>
    /// <param name="positionWS">The world-space position to check around.</param>
    /// <param name="radius">The radius within which to search.</param>
    public void GetSquareGOsWithinRadius(List<GameObject> squareGOs, Vector3 positionWS, float radius)
    {
        // Compute the square of the radius for efficiency.
        float radiusSqr = radius * radius;
        // Iterate over all square GameObjects.
        foreach (GameObject squareGO in allSquaresGO)
        {
            // If the square is within the radius, add it to the provided list.
            if ((squareGO.transform.position - positionWS).sqrMagnitude < radiusSqr)
                squareGOs.Add(squareGO);
        }
    }



    /// <summary>
    /// Sets the active state of all visual pieces.
    /// </summary>
    /// <param name="active">True to enable all pieces; false to disable them.</param>
    public void SetActiveAllPieces(bool active)
    {
        // Retrieve all VisualPiece components in child objects.
        VisualPiece[] visualPiece = GetComponentsInChildren<VisualPiece>(true);
        // Set the enabled state of each VisualPiece.
        foreach (VisualPiece pieceBehaviour in visualPiece)
            pieceBehaviour.enabled = active;
    }





    /// <summary>
    /// Enables only the pieces belonging to the specified side that also have legal moves.
    /// </summary>
    /// <param name="side">The side (White or Black) to enable.</param>
    public void EnsureOnlyPiecesOfSideAreEnabled(Side side)
    {
        // Retrieve all VisualPiece components in child objects.
        VisualPiece[] visualPiece = GetComponentsInChildren<VisualPiece>(true);
        // Loop over each VisualPiece.
        foreach (VisualPiece pieceBehaviour in visualPiece)
        {
            // Get the corresponding chess piece from the board.
            Piece piece = GameManager.Instance.CurrentBoard[pieceBehaviour.CurrentSquare];
            // Enable the piece only if it belongs to the specified side and has legal moves.
            pieceBehaviour.enabled = pieceBehaviour.PieceColor == side
                                     && GameManager.Instance.HasLegalMoves(piece);
        }
    }

    /// <summary>
    /// Destroys the visual representation of a piece at the specified square.
    /// </summary>
    /// <param name="position">The board square from which to destroy the piece.</param>
    public void TryDestroyVisualPiece(Square position)
    {
        if (NetworkManager.Singleton.IsServer)
        {
            // Find the VisualPiece component within the square's GameObject.
            VisualPiece visualPiece = positionMap[position].GetComponentInChildren<VisualPiece>();
            // If a VisualPiece is found, destroy its GameObject immediately.
            if (visualPiece != null)
            {
                //DestroyImmediate(visualPiece.gameObject);
                NetworkObject networkObject = visualPiece.GetComponent<NetworkObject>();
                if (networkObject != null && networkObject.IsSpawned)
                {
                    ulong objectId = networkObject.NetworkObjectId;

                    GameManager.Instance.DestoryPieceServerRpc(objectId);
                }
            }
        }

    }

    /// <summary>
    /// Retrieves the GameObject representing the piece at the given board square.
    /// </summary>
    /// <param name="position">The board square to check.</param>
    /// <returns>The piece GameObject if one exists; otherwise, null.</returns>
    public GameObject GetPieceGOAtPosition(Square position)
    {
        // Get the square GameObject corresponding to the position.
        GameObject square = GetSquareGOByPosition(position);
        // Return the first child GameObject (which represents the piece) if it exists.
        return square.transform.childCount == 0 ? null : square.transform.GetChild(0).gameObject;
    }




    /// <summary>
    /// Computes the world-space position offset for a given file or rank index.
    /// </summary>
    /// <param name="index">The file or rank index (1 to 8).</param>
    /// <returns>The computed offset from the centre of the board plane.</returns>
    private static float FileOrRankToSidePosition(int index)
    {
        // Calculate a normalized parameter (t) based on the index.
        float t = (index - 1) / 7f;
        // Interpolate between the negative and positive half-length of the board side.
        return Mathf.Lerp(-BoardPlaneSideHalfLength, BoardPlaneSideHalfLength, t);
    }

    /// <summary>
    /// Clears all visual pieces from the board.
    /// </summary>
  public void ClearBoard()
{
    VisualPiece[] pieces = GetComponentsInChildren<VisualPiece>(true);
    foreach (VisualPiece visualPiece in pieces)
    {
        if (visualPiece.TryGetComponent(out NetworkObject netObj))
        {
            if (NetworkManager.Singleton.IsServer)
                netObj.Despawn(true);
            else
                Destroy(visualPiece.gameObject);
        }
        else
        {
            Destroy(visualPiece.gameObject);
        }
    }
}


    /// <summary>
    /// Retrieves the GameObject for a board square based on its chess notation.
    /// </summary>
    /// <param name="position">The board square to find.</param>
    /// <returns>The corresponding square GameObject.</returns>
    public GameObject GetSquareGOByPosition(Square position) =>
        Array.Find(allSquaresGO, go => go.name == SquareToString(position));
}
