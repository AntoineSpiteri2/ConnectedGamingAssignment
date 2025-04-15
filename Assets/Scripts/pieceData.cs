using UnityEngine;       // Provides Unity-specific functionalities.
using System;            // Includes base .NET framework classes.
using UnityChess;        // Contains chess-specific classes, including the Piece class.

[Serializable]           // Indicates that this class can be serialized (useful for saving data or for Unity's Inspector).
public class PieceData
{
    // Represents the type of the chess piece as a string (e.g., "Knight", "Queen").
    public string pieceType;
    // Represents the owner of the chess piece as a string (e.g., "White" or "Black").
    public string owner;

    // Default constructor required for serialization.
    public PieceData() { }

    // Constructs a new PieceData instance from a given Piece object.
    // This allows conversion from the runtime Piece object to a simple data format for storage or network transmission.
    public PieceData(Piece piece)
    {
        // Retrieve the type name of the chess piece (e.g., the class name like "Knight" or "Queen").
        pieceType = piece.GetType().Name;
        // Convert the owner of the piece (typically an enum) to its string representation.
        owner = piece.Owner.ToString();
    }
}
