using System;
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


    public SerializablePiece(string type, string owner)
    {
        if (!Enum.TryParse(type, out PieceType parsedType))
        {
            throw new ArgumentException($"Invalid piece type: {type}");
        }

        if (!Enum.TryParse(owner, out Side parsedOwner))
        {
            throw new ArgumentException($"Invalid owner: {owner}");
        }

        this.Type = parsedType;
        this.Owner = parsedOwner;
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