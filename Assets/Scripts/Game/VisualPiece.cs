using System.Collections.Generic;             // Provides the List<T> and other collection types.
using System.Drawing.Text;                     // (Unused; included namespace can be removed if not needed.)
using System.Threading;                        // Provides threading functionality (unused in this snippet).
using System.Threading.Tasks;                  // Provides Task-based asynchronous operations (unused in this snippet).
using Unity.Netcode.Components;               // Provides network components such as NetworkTransform.
using UnityChess;                             // Contains chess-specific types (e.g., Piece, Square, etc.).
using UnityEngine;                            // Provides UnityEngine core classes.
using static UnityChess.SquareUtil;           // Allows static calls to helper methods in SquareUtil (e.g., StringToSquare).

/// <summary>
/// Represents a visual chess piece in the game. This component handles user interaction,
/// such as dragging and dropping pieces, and determines the closest square on the board
/// where the piece should land. It also raises an event when a piece has been moved.
/// </summary>
public class VisualPiece : MonoBehaviour
{
    // Delegate definition for handling the event when a visual piece has been moved.
    // Parameters:
    // - movedPieceInitialSquare: The square where the piece started.
    // - movedPieceTransform: The transform of the moved piece.
    // - closestBoardSquareTransform: The transform of the board square where the piece lands.
    // - promotionPiece (optional): A piece if a pawn promotion occurred.
    public delegate void VisualPieceMovedAction(
        Square movedPieceInitialSquare,
        Transform movedPieceTransform,
        Transform closestBoardSquareTransform,
        Piece promotionPiece = null);

    // Static event raised when a visual piece is moved.
    public static event VisualPieceMovedAction VisualPieceMoved;

    // Indicates the side (e.g., White or Black) to which this piece belongs.
    public Side PieceColor;

    // Property that returns the current board square of the piece.
    // It does so by converting its parent GameObject's name into a Square using a helper.
    public Square CurrentSquare => StringToSquare(transform.parent.name);

    // Constant used as the collision detection radius for potential landing squares.
    // Helps determine the board squares that are "close" to the piece.
    private const float SquareCollisionRadius = 9f; // Adjusted radius value.

    // Reference to the camera that views the board.
    private Camera boardCamera;
    // Stores the screen-space position of the piece when it is initially clicked.
    private Vector3 piecePositionSS;
    // Optional SphereCollider reference for collision handling (if needed).
    private SphereCollider pieceBoundingSphere;
    // A list used to store potential landing square GameObjects during drag/drop.
    [SerializeField] private List<GameObject> potentialLandingSquares;

    // Cached reference to the NetworkTransform component for synchronizing position across the network.
    private NetworkTransform networkTransform;

    // Variables used to store initial square and piece transform during a drag operation.
    private Square initialSquare;
    private Transform correctPieceTransform;

    /// <summary>
    /// Start is called before the first frame update.
    /// Initialises necessary variables and obtains a reference to the main camera.
    /// </summary>
    private void Start()
    {
        // Initialise the list for storing potential landing square GameObjects.
        potentialLandingSquares = new List<GameObject>();
        // Cache the NetworkTransform component attached to this GameObject.
        networkTransform = GetComponent<NetworkTransform>();
        // Get the main camera from the scene; used for raycasting and screen-to-world conversions.
        boardCamera = Camera.main;
    }

    /// <summary>
    /// Called when the user presses the mouse button down over the piece.
    /// Records the initial position in screen space and stores the initial square.
    /// </summary>
    public void OnMouseDown()
    {
        // Check if it's the local player's turn and if the clicked piece belongs to the correct side.
        if (!IsLocalPlayerTurn() || PieceColor != GameManager.Instance.SideToMove)
        {
            if (GameManager.Instance.DebugMode)
            {
                Debug.Log("Invalid piece or not your turn.");
            }
            return;
        }

        // Create a ray from the camera through the mouse position.
        Ray ray = boardCamera.ScreenPointToRay(Input.mousePosition);
        // Check if the ray intersects any collider.
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            // Try to get the VisualPiece component from the collider that was hit.
            VisualPiece clickedPiece = hit.collider.GetComponent<VisualPiece>();
            // Ensure the clicked piece is indeed the one being interacted with.
            if (clickedPiece == this)
            {
                // Store the initial square where this piece resides.
                initialSquare = CurrentSquare;
                // Cache this piece's transform for further use.
                correctPieceTransform = transform;

                // Disable network transform to allow local dragging without network interference.
                if (networkTransform != null)
                    networkTransform.enabled = false;

                // Record the screen-space position (z component holds the distance from camera).
                piecePositionSS = boardCamera.WorldToScreenPoint(transform.position);
            }
            else
            {
                if (GameManager.Instance.DebugMode)
                {
                    Debug.LogWarning("Incorrect piece clicked: " + clickedPiece.name);
                }
            }
        }
    }

    /// <summary>
    /// Helper method to determine if it is the local player's turn.
    /// This method compares the local client ID with the IDs of players and the current turn.
    /// </summary>
    /// <returns>True if it is the local player's turn; otherwise, false.</returns>
    private bool IsLocalPlayerTurn()
    {
        // Retrieve the local client's ID.
        ulong localId = GameManager.Instance.LocalClientId;
        // Get the side whose turn it currently is.
        Side currentTurn = GameManager.Instance.SideToMove;

        // Adjust the localId if it is larger than expected.
        if (localId > 1)
        {
            localId = 1;
        }

        // Ensure there are at least two players connected.
        if (GameManager.Instance.connectedPlayers.Count < 2)
        {
            if (GameManager.Instance.DebugMode)
            {
                Debug.LogWarning("[CLIENT] Not enough players connected to determine turn.");
            }
            return false;
        }

        // Get the IDs for the white and black sides.
        ulong whiteId = GameManager.Instance.connectedPlayers[0];
        ulong blackId = GameManager.Instance.connectedPlayers[1];

        if (GameManager.Instance.DebugMode)
        {
            Debug.Log($"[CLIENT] My ID: {localId}, White: {whiteId}, Black: {blackId}, Current Turn: {currentTurn}");
        }

        // Return true only if the local player matches the required ID for the current turn.
        return (currentTurn == Side.White && localId == whiteId) ||
               (currentTurn == Side.Black && localId == blackId);
    }

    /// <summary>
    /// Called while the user drags the mouse with the piece.
    /// Updates the piece's position to follow the mouse cursor.
    /// </summary>
    private void OnMouseDrag()
    {
        // Only allow dragging if it is the local player's turn.
        if (!IsLocalPlayerTurn()) return;
        if (PieceColor != GameManager.Instance.SideToMove)
        {
            if (GameManager.Instance.DebugMode)
            {
                Debug.Log("Not your piece!");
            }
            return;
        }

        // Create a new screen space position keeping the original z value (distance from camera).
        Vector3 nextPiecePositionSS = new Vector3(Input.mousePosition.x, Input.mousePosition.y, piecePositionSS.z);
        // Convert the new screen space position to world space.
        Vector3 newWorldPos = boardCamera.ScreenToWorldPoint(nextPiecePositionSS);
        // Update the piece's position.
        transform.position = newWorldPos;
        // Also update the NetworkTransform's position to keep network state in sync.
        networkTransform.transform.position = newWorldPos;
    }

    /// <summary>
    /// Called when the user releases the mouse button after dragging the piece.
    /// Determines the closest board square to where the piece was dropped and raises an event.
    /// </summary>
    public void OnMouseUp()
    {
        // Validate that the piece belongs to the correct side.
        if (PieceColor != GameManager.Instance.SideToMove)
        {
            if (GameManager.Instance.DebugMode)
            {
                Debug.Log("Not your piece!");
            }
            return;
        }

        // Ensure it is the local player's turn.
        if (!IsLocalPlayerTurn())
        {
            if (GameManager.Instance.DebugMode)
            {
                Debug.Log("[CLIENT] Move Rejected - Not Your Turn!");
            }
            // Reset the piece's position to its parent's position if not allowed to move.
            transform.position = transform.parent.position;
            return;
        }

        // If the component is enabled, proceed with determining the landing square.
        if (enabled)
        {
            // Clear any previously stored potential landing squares.
            potentialLandingSquares.Clear();
            // Retrieve all square GameObjects within the defined collision radius of the piece's current position.
            BoardManager.Instance.GetSquareGOsWithinRadius(potentialLandingSquares, transform.position, SquareCollisionRadius);

            // If no valid squares are found, reset the piece's position and exit.
            if (potentialLandingSquares.Count == 0)
            {
                if (GameManager.Instance.DebugMode)
                {
                    Debug.Log("[CLIENT] Move Rejected - No Valid Squares Found!");
                }
                transform.position = transform.parent.position;
                return;
            }

            // Find the closest square by comparing distances.
            Transform closestSquareTransform = potentialLandingSquares[0].transform;
            float shortestDistanceFromPieceSquared = (closestSquareTransform.position - transform.position).sqrMagnitude;

            // Iterate over potential squares to find the one with the minimum squared distance.
            for (int i = 1; i < potentialLandingSquares.Count; i++)
            {
                GameObject potentialLandingSquare = potentialLandingSquares[i];
                float distanceFromPieceSquared = (potentialLandingSquare.transform.position - transform.position).sqrMagnitude;

                if (distanceFromPieceSquared < shortestDistanceFromPieceSquared)
                {
                    shortestDistanceFromPieceSquared = distanceFromPieceSquared;
                    closestSquareTransform = potentialLandingSquare.transform;
                }
            }
            if (GameManager.Instance.DebugMode)
            {
                Debug.Log($"[CLIENT] Attempting Move from {CurrentSquare} to {closestSquareTransform.name}");
            }

            // Optionally, retrieve the piece GameObject from the board logic.
            GameObject movedPiece = BoardManager.Instance.GetPieceGOAtPosition(CurrentSquare);
            // Retrieve the corresponding Piece from the game board model.
            Piece piece = GameManager.Instance.CurrentBoard[CurrentSquare];
            // Determine the destination square from the closest square's name.
            Square destinationSquare = StringToSquare(closestSquareTransform.name);

            // Raise the VisualPieceMoved event with the initial square, transform, and closest square transform.
            VisualPieceMoved?.Invoke(initialSquare, correctPieceTransform, closestSquareTransform);

            // Re-enable the NetworkTransform once the move is finished to resume network synchronization.
            NetworkTransform netTransform = GetComponent<NetworkTransform>();
            if (netTransform != null)
            {
                netTransform.enabled = true;
            }
        }
    }
}
