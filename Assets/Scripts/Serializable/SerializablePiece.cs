using Unity.Netcode;
using UnityChess;

public enum PieceType : byte
{
    None = 0,
    Pawn,
    Knight,
    Bishop,
    Rook,
    Queen,
    King
}

public struct SerializablePiece : INetworkSerializable
{
    public PieceType Type;
    public Side Owner;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Type);
        serializer.SerializeValue(ref Owner);
    }

    public static SerializablePiece FromPiece(Piece piece) => new SerializablePiece
    {
        Type = piece switch
        {
            Pawn => PieceType.Pawn,
            Knight => PieceType.Knight,
            Bishop => PieceType.Bishop,
            Rook => PieceType.Rook,
            Queen => PieceType.Queen,
            King => PieceType.King,
            _ => PieceType.None
        },
        Owner = piece.Owner
    };

    public Piece ToPiece() => Type switch
    {
        PieceType.Pawn => new Pawn(Owner),
        PieceType.Knight => new Knight(Owner),
        PieceType.Bishop => new Bishop(Owner),
        PieceType.Rook => new Rook(Owner),
        PieceType.Queen => new Queen(Owner),
        PieceType.King => new King(Owner),
        _ => null
    };
}