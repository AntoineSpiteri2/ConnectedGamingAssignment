using Unity.Netcode;
using UnityChess;
using UnityEngine;

/// <summary>
/// A simple struct that Unity's JsonUtility can serialize.
/// It can convert to/from your custom Square class.
/// </summary>
[System.Serializable]
public struct SerializableSquare : INetworkSerializable

{
    public int file;
    public int rank;

    public SerializableSquare(int file, int rank)
    {
        this.file = file;
        this.rank = rank;
    }

    /// <summary>
    /// Convert this serializable struct back into the engine’s Square object.
    /// </summary>
    public Square ToSquare()
    {
        return new Square(file, rank);
    }

    /// <summary>
    /// Convert from the engine’s Square to this serializable struct.
    /// </summary>
    public static SerializableSquare FromSquare(Square square)
    {
        return new SerializableSquare(square.File, square.Rank);
    }

    public override string ToString() => $"({file}, {rank})";

    /// <summary>
    /// NGO calls this to read/write the data.
    /// </summary>
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref file);
        serializer.SerializeValue(ref rank);
    }
}
