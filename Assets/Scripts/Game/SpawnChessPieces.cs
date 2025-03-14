using UnityEngine;

[CreateAssetMenu(fileName = "ChessPieceData", menuName = "ScriptableObjects/ChessPieceData", order = 1)]
public class ChessPieceData : ScriptableObject
{
    public GameObject[] chessPiecePrefab;
    public Transform[] spawnTransform; // to track position of each piece for server sync
}
