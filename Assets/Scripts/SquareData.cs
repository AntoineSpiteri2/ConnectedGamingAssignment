using UnityEngine;
using System;
using UnityChess;

[Serializable]
public class SquareData
{
    public int file;
    public int rank;

    // Default constructor for deserialization.
    public SquareData() { }

    // Conversion from your Square struct.
    public SquareData(Square square)
    {
        file = square.File;
        rank = square.Rank;
    }

    // Helper to convert back.
    public Square ToSquare()
    {
        return new Square(file, rank);
    }
}
