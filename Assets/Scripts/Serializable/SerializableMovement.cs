using Unity.Netcode;
using UnityChess;

public struct SerializableMovement : INetworkSerializable
{
    public SerializableSquare Start;
    public SerializableSquare End;
    public SerializablePiece PromotionPiece; // Handles promoted piece type
    public bool IsPromotion; // Explicit flag for move type

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Start);
        serializer.SerializeValue(ref End);
        serializer.SerializeValue(ref PromotionPiece);
        serializer.SerializeValue(ref IsPromotion);
    }

    public static SerializableMovement FromMovement(Movement move)
    {
        bool isPromotion = move is PromotionMove;
        return new SerializableMovement
        {
            Start = SerializableSquare.FromSquare(move.Start),
            End = SerializableSquare.FromSquare(move.End),
            PromotionPiece = isPromotion
                ? SerializablePiece.FromPiece(((PromotionMove)move).PromotionPiece)
                : default,
            IsPromotion = isPromotion
        };
    }

    public Movement ToMovement()
    {
        Movement baseMove = new Movement(Start.ToSquare(), End.ToSquare());

        if (IsPromotion)
        {
            var promotionMove = new PromotionMove(baseMove); // Use the new constructor
            promotionMove.SetPromotionPiece(PromotionPiece.ToPiece());
            return promotionMove;
        }

        return baseMove;
    }
}