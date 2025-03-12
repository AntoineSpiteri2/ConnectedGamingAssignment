using System.Collections.Generic;
using Unity.Netcode.Components;
using UnityChess;
using UnityEngine;
using static UnityChess.SquareUtil;

/// <summary>
/// Represents a visual chess piece in the game. This component handles user interaction,
/// such as dragging and dropping pieces, and determines the closest square on the board
/// where the piece should land. It also raises an event when a piece has been moved.
/// </summary>
public class VisualPiece : MonoBehaviour {
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
     private const float SquareCollisionRadius = 9f;
	
	// The camera used to view the board.
	private Camera boardCamera;
	// The screen-space position of the piece when it is first picked up.
	private Vector3 piecePositionSS;
	// A reference to the piece's SphereCollider (if required for collision handling).
	private SphereCollider pieceBoundingSphere;
    // A list to hold potential board square GameObjects that the piece might land on.
    [SerializeField]  private List<GameObject> potentialLandingSquares;

	//[SerializeField] private int OwnerNum; // 0 for white (host), 1 for black (client) as allow the host or client to move the pieces and not both
    // A cached reference to the transform of this piece.
    private NetworkTransform networkTransform;

	/// <summary>
	/// Initialises the visual piece. Sets up necessary variables and obtains a reference to the main camera.
	/// </summary>
	private void Start() {
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
    public void OnMouseDown()
    {
        if (!IsLocalPlayerTurn())
        {
            Debug.Log("Not your turn!");
            return;
        }
        if (networkTransform != null)
        {
            networkTransform.enabled = false;
        }
        piecePositionSS = boardCamera.WorldToScreenPoint(transform.position);
    }


    private bool IsLocalPlayerTurn()
    {
        ulong localId = GameManager.Instance.LocalClientId;
        if (GameManager.Instance.connectedPlayers.Count < 2) return false;
        if (GameManager.Instance.isWhiteTurn)
            return localId == GameManager.Instance.connectedPlayers[0];
        else
            return localId == GameManager.Instance.connectedPlayers[1];
    }


    /// <summary>
    /// Called while the user drags the piece with the mouse.
    /// Updates the piece's world position to follow the mouse cursor.
    /// </summary>
    private void OnMouseDrag()
    {
        if (!IsLocalPlayerTurn()) return;
        Vector3 nextPiecePositionSS = new Vector3(Input.mousePosition.x, Input.mousePosition.y, piecePositionSS.z);
        Vector3 newWorldPos = boardCamera.ScreenToWorldPoint(nextPiecePositionSS);
        transform.position = newWorldPos;
    }

    /// <summary>
    /// Called when the user releases the mouse button after dragging the piece.
    /// Determines the closest board square to the piece and raises an event with the move.
    /// </summary>
    public void OnMouseUp() {
		if (enabled) {
			// Clear any previous potential landing square candidates.
			potentialLandingSquares.Clear();
			// Obtain all square GameObjects within the collision radius of the piece's current position.
			BoardManager.Instance.GetSquareGOsWithinRadius(potentialLandingSquares, transform.position, SquareCollisionRadius);

			// If no squares are found, assume the piece was moved off the board and reset its position.
			if (potentialLandingSquares.Count == 0) { // piece moved off board
                transform.position = transform.parent.position;
				return;
			}
	
			// Determine the closest square from the list of potential landing squares.
			Transform closestSquareTransform = potentialLandingSquares[0].transform;
			// Calculate the square of the distance between the piece and the first candidate square.
			float shortestDistanceFromPieceSquared = (closestSquareTransform.position - transform.position).sqrMagnitude;
			
			// Iterate through remaining potential squares to find the closest one.
			for (int i = 1; i < potentialLandingSquares.Count; i++) {
				GameObject potentialLandingSquare = potentialLandingSquares[i];
				// Calculate the squared distance from the piece to the candidate square.
				float distanceFromPieceSquared = (potentialLandingSquare.transform.position - transform.position).sqrMagnitude;

				// If the current candidate is closer than the previous closest, update the closest square.
				if (distanceFromPieceSquared < shortestDistanceFromPieceSquared) {
					shortestDistanceFromPieceSquared = distanceFromPieceSquared;
					closestSquareTransform = potentialLandingSquare.transform;
				}
			}

			// Raise the VisualPieceMoved event with the initial square, the piece's transform, and the closest square transform.
			VisualPieceMoved?.Invoke(CurrentSquare, transform, closestSquareTransform);
		}
	}
}
