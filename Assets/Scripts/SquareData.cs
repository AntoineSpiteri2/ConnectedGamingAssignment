using UnityEngine;       // Provides Unity-specific classes and functionality.
using System;            // Provides base .NET classes.
using UnityChess;        // Likely contains chess-specific types such as the Square struct.

[Serializable]           // Allows this class to be serialized, which is useful for saving data or for Unity's Inspector.
public class SquareData
{
    // The file (column) index of the square, typically 0-7 for a chess board.
    public int file;
    // The rank (row) index of the square, typically 0-7 for a chess board.
    public int rank;

    // Default constructor is required for deserialization and instantiation without parameters.
    public SquareData() { }

    // Constructor that initializes SquareData from a given Square instance.
    // This enables conversion from a runtime Square to its serializable representation.
    public SquareData(Square square)
    {
        // Assign the file (column) from the Square's File property.
        file = square.File;
        // Assign the rank (row) from the Square's Rank property.
        rank = square.Rank;
    }

    // Helper method to convert this SquareData instance back to a runtime Square object.
    public Square ToSquare()
    {
        // Create and return a new Square object using the stored file and rank values.
        return new Square(file, rank);
    }
}
