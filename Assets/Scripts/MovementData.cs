using UnityEngine;
using System;
using UnityChess;

[Serializable]
public class MovementData
{
    public SquareData start;
    public SquareData end;

    public MovementData() { }

    public MovementData(Movement move)
    {
        start = new SquareData(move.Start);
        end = new SquareData(move.End);
    }

    public Movement ToMovement()
    {
        return new Movement(start.ToSquare(), end.ToSquare());
    }
}
