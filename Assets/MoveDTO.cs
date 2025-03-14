using UnityEngine;
using System;
using UnityChess;

[Serializable]
public class MoveDTO
{
    public SquareData initialSquare;      // For the square representing the piece's initial position.
    public TransformData startTransform;  // Data from movedPieceTransform.
    public TransformData endTransform;    // Data from closestBoardSquareTransform.
    public PieceData promotionPiece;      // Optional; may be null.

    public MoveDTO() { }

    public MoveDTO(Square movedPieceInitialSquare, Transform movedPieceTransform, Transform closestBoardSquareTransform, Piece promotionPiece = null)
    {
        initialSquare = new SquareData(movedPieceInitialSquare);
        startTransform = new TransformData(movedPieceTransform);
        endTransform = new TransformData(closestBoardSquareTransform);
        if (promotionPiece != null)
            this.promotionPiece = new PieceData(promotionPiece);
    }
}
