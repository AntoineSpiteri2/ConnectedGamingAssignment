using UnityEngine;           // Provides Unity-specific classes and functionality.
using System;                // Provides base system classes.
using UnityChess;            // Likely contains chess-specific data structures (e.g., Movement, Square).

[Serializable]               // Marks the class as serializable for Unity (e.g., for JSON or Inspector display).
public class MovementData
{
    // The starting square data of a chess move.
    public SquareData start;
    // The ending square data of a chess move.
    public SquareData end;

    // Default constructor, required for serialization and instantiation without parameters.
    public MovementData() { }

    // Constructor that creates MovementData from a Movement object.
    // It converts the Movement's start and end squares to their serializable SquareData representations.
    public MovementData(Movement move)
    {
        start = new SquareData(move.Start);
        end = new SquareData(move.End);
    }

    // Converts this MovementData instance back into a Movement object.
    // It calls the ToSquare() method on both start and end SquareData to retrieve non-serializable Square instances.
    public Movement ToMovement()
    {
        return new Movement(start.ToSquare(), end.ToSquare());
    }
}
