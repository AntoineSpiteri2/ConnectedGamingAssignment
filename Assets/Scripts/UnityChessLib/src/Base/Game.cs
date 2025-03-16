using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace UnityChess {
	/// <summary>Representation of a standard chess game including a history of moves made.</summary>
	public class Game {
		public Timeline<GameConditions> ConditionsTimeline { get; }
		public Timeline<Board> BoardTimeline { get; }
		public Timeline<HalfMove> HalfMoveTimeline { get; }
		public Timeline<Dictionary<Piece, Dictionary<(Square, Square), Movement>>> LegalMovesTimeline { get; }

		/// <summary>Creates a Game instance of a given mode with a standard starting Board.</summary>
		public Game() : this(GameConditions.NormalStartingConditions, Board.StartingPositionPieces) { }

		public Game(GameConditions startingConditions, params (Square, Piece)[] squarePiecePairs) {
			Board startingBoard = new Board(squarePiecePairs);
			BoardTimeline = new Timeline<Board> { startingBoard };
			HalfMoveTimeline = new Timeline<HalfMove>();
			ConditionsTimeline = new Timeline<GameConditions> { startingConditions };
			LegalMovesTimeline = new Timeline<Dictionary<Piece, Dictionary<(Square, Square), Movement>>> {
				CalculateLegalMovesForPosition(startingBoard, startingConditions)
			};
		}

		/// <summary>Executes passed move and switches sides; also adds move to history.</summary>
		public bool TryExecuteMove(Movement move) {
			if (!TryGetLegalMove(move.Start, move.End, out Movement validatedMove)) {
				return false;
			}

			//create new copy of previous current board, and execute the move on it
			BoardTimeline.TryGetCurrent(out Board boardBeforeMove);
			Board resultingBoard = new Board(boardBeforeMove);
			resultingBoard.MovePiece(validatedMove);
			BoardTimeline.AddNext(resultingBoard);
			
			ConditionsTimeline.TryGetCurrent(out GameConditions conditionsBeforeMove); 
			Side updatedSideToMove = conditionsBeforeMove.SideToMove.Complement();
			bool causedCheck = Rules.IsPlayerInCheck(resultingBoard, updatedSideToMove);
			bool capturedPiece = boardBeforeMove[validatedMove.End] != null || validatedMove is EnPassantMove;
			
			HalfMove halfMove = new HalfMove(boardBeforeMove[validatedMove.Start], validatedMove, capturedPiece, causedCheck);
			GameConditions resultingGameConditions = conditionsBeforeMove.CalculateEndingConditions(boardBeforeMove, halfMove);
			ConditionsTimeline.AddNext(resultingGameConditions);

			Dictionary<Piece, Dictionary<(Square, Square), Movement>> legalMovesByPiece
				= CalculateLegalMovesForPosition(resultingBoard, resultingGameConditions);

			int numLegalMoves = GetNumLegalMoves(legalMovesByPiece);

			LegalMovesTimeline.AddNext(legalMovesByPiece);

			halfMove.SetGameEndBools(
				Rules.IsPlayerStalemated(resultingBoard, updatedSideToMove, numLegalMoves),
				Rules.IsPlayerCheckmated(resultingBoard, updatedSideToMove, numLegalMoves)
			);
			HalfMoveTimeline.AddNext(halfMove);
			
			return true;
		}

        public bool TryGetLegalMove(Square startSquare, Square endSquare, out Movement move)
        {
            move = null;
            if (!BoardTimeline.TryGetCurrent(out Board currentBoard))
            {
                System.Diagnostics.Debug.WriteLine("[GAME] No current board found.");
                return false;
            }
            if (!LegalMovesTimeline.TryGetCurrent(out Dictionary<Piece, Dictionary<(Square, Square), Movement>> currentLegalMoves))
            {
                System.Diagnostics.Debug.WriteLine("[GAME] No current legal moves found.");
                return false;
            }
            if (!(currentBoard[startSquare] is Piece movingPiece))
            {
                System.Diagnostics.Debug.WriteLine($"[GAME] No piece found at {startSquare}.");
                return false;
            }
            if (!currentLegalMoves.TryGetValue(movingPiece, out Dictionary<(Square, Square), Movement> movesByStartEndSquares))
            {
                System.Diagnostics.Debug.WriteLine($"[GAME] No legal moves recorded for piece {movingPiece} at {startSquare}.");
                return false;
            }
            if (!movesByStartEndSquares.TryGetValue((startSquare, endSquare), out move))
            {
                System.Diagnostics.Debug.WriteLine($"[GAME] Move from {startSquare} to {endSquare} not found. Available moves for this piece:");
                foreach (var key in movesByStartEndSquares.Keys)
                {
                    System.Diagnostics.Debug.WriteLine($"  From {key.Item1} to {key.Item2}");
                }
                return false;
            }
            return true;
        }


        public bool TryGetLegalMovesForPiece(Piece movingPiece, out ICollection<Movement> legalMoves) {
			legalMoves = null;

			if (movingPiece != null
			    && LegalMovesTimeline.TryGetCurrent(out Dictionary<Piece, Dictionary<(Square, Square), Movement>> legalMovesByPiece)
			    && legalMovesByPiece.TryGetValue(movingPiece, out Dictionary<(Square, Square), Movement> movesByStartEndSquares)
			    && movesByStartEndSquares != null
			) {
				legalMoves = movesByStartEndSquares.Values;
				return true;
			}



			return false;
		}

        public void RecalculateLegalMoves()
        {
            if (!BoardTimeline.TryGetCurrent(out Board board) ||
                !ConditionsTimeline.TryGetCurrent(out GameConditions gameConditions))
            {
                Debug.WriteLine("[Game] Unable to recalculate legal moves: No valid board or game conditions.");
                return;
            }

            Dictionary<Piece, Dictionary<(Square, Square), Movement>> updatedLegalMoves =
                CalculateLegalMovesForPosition(board, gameConditions);

            LegalMovesTimeline.AddNext(updatedLegalMoves);
        }


        public bool ResetGameToHalfMoveIndex(int halfMoveIndex) {
			if (HalfMoveTimeline.HeadIndex == -1) {
				return false;
			}

			BoardTimeline.HeadIndex = halfMoveIndex + 1;
			ConditionsTimeline.HeadIndex = halfMoveIndex + 1;
			LegalMovesTimeline.HeadIndex = halfMoveIndex + 1;
			HalfMoveTimeline.HeadIndex = halfMoveIndex;

			return true;
		}
		
		public static int GetNumLegalMoves(Dictionary<Piece, Dictionary<(Square, Square), Movement>> legalMovesByPiece) {
			int result = 0;
			
			if (legalMovesByPiece != null) {
				foreach (Dictionary<(Square, Square), Movement> movesByStartEndSquares in legalMovesByPiece.Values) {
					result += movesByStartEndSquares.Count;
				}
			}

			return result;
		}
        public static Dictionary<Piece, Dictionary<(Square, Square), Movement>> CalculateLegalMovesForPosition(
    Board board,
    GameConditions gameConditions
)
        {
            Dictionary<Piece, Dictionary<(Square, Square), Movement>> result = null;

            for (int file = 1; file <= 8; file++)
            {
                for (int rank = 1; rank <= 8; rank++)
                {
                    if (board[file, rank] is Piece piece
                        && piece.Owner == gameConditions.SideToMove
                        && piece.CalculateLegalMoves(board, gameConditions, new Square(file, rank)) is
                        { } movesByStartEndSquares
                    )
                    {
                        if (result == null)
                        {
                            result = new Dictionary<Piece, Dictionary<(Square, Square), Movement>>();
                        }

                        if (piece is King)
                        {
                            movesByStartEndSquares = movesByStartEndSquares
                                .Where(kvp => {
                                    // Simulate the move
                                    Board testBoard = new Board(board);
                                    testBoard.MovePiece(kvp.Value);

                                    // If the king is still in check after the move, discard it
                                    bool kingStillInCheck = Rules.IsPlayerInCheck(testBoard, piece.Owner);
                                    if (kingStillInCheck)
                                    {
                                        Debug.WriteLine($"[DEBUG] Removing illegal king move: {piece} from {kvp.Key.Item1} to {kvp.Key.Item2} (King still in check)");
                                    }

                                    return !kingStillInCheck; // Keep only safe moves
                                })
                                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        }

                        result[piece] = movesByStartEndSquares;
                    }
                }
            }

            return result;
        }

    }
}