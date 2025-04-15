using UnityEngine;           // Provides Unity-specific classes and functionality.
using System;                // Includes base .NET classes.
using UnityChess;            // Likely contains chess-specific types and utilities.

[System.Serializable]        // Marks the class as serializable so it can be easily stored and transferred (e.g., in JSON or Unity Inspector).
public class MoveDTO
{
    // Represents the starting square of the move in a format that can be serialized.
    public SerializableSquare initialSquare;

    // The name of the transform for the chess piece that is being moved.
    public string pieceTransformName;

    // The name of the transform for the destination square where the piece is moved.
    public string destinationSquareTransformName;

    // For pawn promotions: indicates the type of piece the pawn is promoted to.
    public string promotionPieceType;
}
