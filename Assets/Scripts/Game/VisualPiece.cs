using System.Collections.Generic;
using System.Drawing.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode.Components;
using UnityChess;
using UnityEngine;
using static UnityChess.SquareUtil;

/// <summary>
/// Represents a visual chess piece in the game. This component handles user interaction,
/// such as dragging and dropping pieces, and determines the closest square on the board
/// where the piece should land. It also raises an event when a piece has been moved.
/// </summary>
public class VisualPiece : MonoBehaviour
{
    // Delegate for handling the event when a visual piece has been moved.
    // Parameters: the initial square of the piece, its transform, the closest square's transform,
    // and an optional promotion piece.
    public delegate void VisualPieceMovedAction(Square movedPieceInitialSquare, Transform movedPieceTransform, Transform closestBoardSquareTransform, Piece promotionPiece = null);

    // Static event raised when a visual piece is moved.
    public static event VisualPieceMovedAction VisualPieceMoved;

    // The colour (side) of the piece (White or Black).
    public Side PieceColor;

    // Retrieves the current board square of the piece by converting its parent's name into a Square.
    public Square CurrentSquare => StringToSquare(transform.parent.name);

    // The radius used to detect nearby board squares for collision detection.
    private const float SquareCollisionRadius = 9f; // reduced from 9f

    // The camera used to view the board.
    private Camera boardCamera;
    // The screen-space position of the piece when it is first picked up.
    private Vector3 piecePositionSS;
    // A reference to the piece's SphereCollider (if required for collision handling).
    private SphereCollider pieceBoundingSphere;
    // A list to hold potential board square GameObjects that the piece might land on.
    [SerializeField] private List<GameObject> potentialLandingSquares;

    //[SerializeField] private int OwnerNum; // 0 for white (host), 1 for black (client) as allow the host or client to move the pieces and not both
    // A cached reference to the transform of this piece.
    private NetworkTransform networkTransform;

    /// <summary>
    /// Initialises the visual piece. Sets up necessary variables and obtains a reference to the main camera.
    /// </summary>
    private void Start()
    {
        // Initialise the list to hold potential landing squares.
        potentialLandingSquares = new List<GameObject>();
        // Cache the transform of this GameObject for efficiency.
        networkTransform = GetComponent<NetworkTransform>();
        // Obtain the main camera from the scene.
        boardCamera = Camera.main;


    }

    /// <summary>
    /// Called when the user presses the mouse button over the piece.
    /// Records the initial screen-space position of the piece.
    /// </summary>
    private Square initialSquare;
    private Transform correctPieceTransform;

    public void OnMouseDown()
    {
        if (!IsLocalPlayerTurn() || PieceColor != GameManager.Instance.SideToMove)
        {
            if (GameManager.Instance.DebugMode) Debug.Log("Invalid piece or not your turn.");
            return;
        }

        Ray ray = boardCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            VisualPiece clickedPiece = hit.collider.GetComponent<VisualPiece>();
            if (clickedPiece == this)
            {
                initialSquare = CurrentSquare;
                correctPieceTransform = transform;

                //Debug.Log("Correct piece selected: " + initialSquare);

                if (networkTransform != null)
                    networkTransform.enabled = false;

                piecePositionSS = boardCamera.WorldToScreenPoint(transform.position);
            }
            else
            {
                if (GameManager.Instance.DebugMode) Debug.LogWarning("Incorrect piece clicked: " + clickedPiece.name);
            }
        }
    }


    private bool IsLocalPlayerTurn()
    {
        ulong localId = GameManager.Instance.LocalClientId;
        Side currentTurn = GameManager.Instance.SideToMove; // Get side to move from GameManager

        if (GameManager.Instance.connectedPlayers.Count < 2)
        {
            if (GameManager.Instance.DebugMode) Debug.LogWarning("[CLIENT] Not enough players connected to determine turn.");
            return false;
        }

        bool isLocalTurn = (currentTurn == Side.White && localId == GameManager.Instance.connectedPlayers[0]) ||
                           (currentTurn == Side.Black && localId == GameManager.Instance.connectedPlayers[1]);


        return isLocalTurn;
    }


    /// <summary>
    /// Called while the user drags the piece with the mouse.
    /// Updates the piece's world position to follow the mouse cursor.
    /// </summary>
    private void OnMouseDrag()
    {
        if (!IsLocalPlayerTurn()) return;
        if (PieceColor != GameManager.Instance.SideToMove)
        {
            if (GameManager.Instance.DebugMode) Debug.Log("Not your piece!");
            return;
        }
        Vector3 nextPiecePositionSS = new Vector3(Input.mousePosition.x, Input.mousePosition.y, piecePositionSS.z);
        Vector3 newWorldPos = boardCamera.ScreenToWorldPoint(nextPiecePositionSS);
        transform.position = newWorldPos;
        networkTransform.transform.position = newWorldPos;

    }


    /// <summary>
    /// Called when the user releases the mouse button after dragging the piece.
    /// Determines the closest board square to the piece and raises an event with the move.
    /// </summary>


    public void OnMouseUp()
    {

        if (PieceColor != GameManager.Instance.SideToMove)
        {
            if (GameManager.Instance.DebugMode) Debug.Log("Not your piece!");
            return;
        }

        if (!IsLocalPlayerTurn())
        {
            if (GameManager.Instance.DebugMode) Debug.Log("[CLIENT] Move Rejected - Not Your Turn!");
            transform.position = transform.parent.position; // Reset piece position
            return;
        }
        if (enabled)
        {
            potentialLandingSquares.Clear();
            BoardManager.Instance.GetSquareGOsWithinRadius(potentialLandingSquares, transform.position, SquareCollisionRadius);

            if (potentialLandingSquares.Count == 0)
            {
                if (GameManager.Instance.DebugMode) Debug.Log("[CLIENT] Move Rejected - No Valid Squares Found!");

                transform.position = transform.parent.position;
                return;
            }

            Transform closestSquareTransform = potentialLandingSquares[0].transform;
            float shortestDistanceFromPieceSquared = (closestSquareTransform.position - transform.position).sqrMagnitude;

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
            if (GameManager.Instance.DebugMode) Debug.Log($"[CLIENT] Attempting Move from {CurrentSquare} to {closestSquareTransform.name}");

            GameObject Movedpiece = BoardManager.Instance.GetPieceGOAtPosition(CurrentSquare);
            // Access the piece from the board logic.
            Piece piece = GameManager.Instance.CurrentBoard[CurrentSquare];
            Square destinationSquare = StringToSquare(closestSquareTransform.name);


            VisualPieceMoved?.Invoke(initialSquare, correctPieceTransform, closestSquareTransform);


            // Re-enable Network Transform after move is finished
            NetworkTransform netTransform = GetComponent<NetworkTransform>();
                if (netTransform != null)
                {
                    netTransform.enabled = true;
                }










        }


    }




}
