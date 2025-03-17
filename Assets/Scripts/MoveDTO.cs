using UnityEngine;
using System;
using UnityChess;

[System.Serializable]
public class MoveDTO
{
    public SerializableSquare initialSquare;
    public string pieceTransformName;
    public string destinationSquareTransformName;

    // For promotions
    public string promotionPieceType;
}

