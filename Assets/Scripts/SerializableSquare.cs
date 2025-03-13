using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityChess;
using UnityEngine;

public struct SerializableSquare : INetworkSerializable
{

    public int File;
    public int Rank;
    // Implement the NetworkSerialize method
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref File);
        serializer.SerializeValue(ref Rank);
    }



    // Convert to/from the library's Square
    public static SerializableSquare FromSquare(Square square) => new SerializableSquare { File = square.File, Rank = square.Rank };
    public Square ToSquare() => new Square(File, Rank);

}
