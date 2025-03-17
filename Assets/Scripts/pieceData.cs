using UnityEngine;
using System;
using UnityChess;

[Serializable]
public class PieceData
{
    public string pieceType;
    public string owner;

    public PieceData() { }

    public PieceData(Piece piece)
    {
        pieceType = piece.GetType().Name; // e.g. "Knight", "Queen", etc.
        owner = piece.Owner.ToString();    // Assuming Owner is an enum (e.g., "White"/"Black")
    }
}
