using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts
{
    public class EightBotBrain : MonoBehaviour
    {
        [SerializeField] private TextAsset data8Json;

        private Data8Book _book;

        public EightGameState CurrentState => GameConfig.CurrentState;

        public void ApplyMove(EightMove move)
        {
            GameConfig.CurrentState = EightGameRules.ApplyMove(GameConfig.CurrentState, move);
        }

        public EightMove? GetOptimalMove()
        {
            var state = GameConfig.CurrentState;
            var legalMoves = EightGameRules.GetLegalMoves(state);
            if (legalMoves.Count == 0) return null;

            EnsureBookLoaded();
            var perspectiveIsWhite = state.WhiteTurn;
            var allowedNextStates = new HashSet<EightGameState>();
            if (_book != null)
            {
                var nextStatesFromBook = _book.GetPossibleNextStates(state);
                foreach (var next in nextStatesFromBook)
                {
                    allowedNextStates.Add(next.NormalizeForCalculation());
                }
            }

            var useBookEdgeFilter = allowedNextStates.Count > 0;
            var bestMove = legalMoves[0];
            var bestScore = int.MinValue;
            var foundByBook = false;

            foreach (var move in legalMoves)
            {
                var child = EightGameRules.ApplyMove(state, move);
                if (useBookEdgeFilter && !allowedNextStates.Contains(child.NormalizeForCalculation()))
                {
                    continue;
                }

                var score = GetOutcomeRankForPerspective(child, perspectiveIsWhite);
                if (score > bestScore || (score == bestScore && ShouldPreferTieBreakMove(state, move, bestMove)))
                {
                    bestScore = score;
                    bestMove = move;
                    foundByBook = true;
                }
            }

            if (useBookEdgeFilter && !foundByBook)
            {
                // If rule expansion and book transitions disagree, keep bot playable by falling back.
                Debug.LogWarning("Bot falling back to rule-based move: Book transition mismatch.");
                foreach (var move in legalMoves)
                {
                    var child = EightGameRules.ApplyMove(state, move);
                    var score = GetOutcomeRankForPerspective(child, perspectiveIsWhite);
                    if (score > bestScore || (score == bestScore && ShouldPreferTieBreakMove(state, move, bestMove)))
                    {
                        bestScore = score;
                        bestMove = move;
                    }
                }
            }

            return bestMove;
        }

        public IReadOnlyList<EightGameState> GetAllPossibleNextStatesFromData8()
        {
            EnsureBookLoaded();
            if (_book == null)
            {
                return new List<EightGameState>();
            }

            return _book.GetPossibleNextStates(GameConfig.CurrentState);
        }

        public bool TryGetCurrentStateClassFromData8(out string stateClass)
        {
            EnsureBookLoaded();
            if (_book == null)
            {
                stateClass = null;
                return false;
            }

            return _book.TryGetClass(GameConfig.CurrentState, out stateClass);
        }

        private void EnsureBookLoaded()
        {
            if (_book != null) return;
            if (data8Json == null) return;

            try
            {
                _book = Data8Book.LoadFromText(data8Json.text);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to parse data8.json: {ex.Message}");
            }
        }

        private int GetOutcomeRankForPerspective(EightGameState state, bool perspectiveIsWhite)
        {
            var sideToMoveRank = GetOutcomeRankForSideToMove(state);
            return perspectiveIsWhite == state.WhiteTurn ? sideToMoveRank : -sideToMoveRank;
        }

        private int GetOutcomeRankForSideToMove(EightGameState state)
        {
            EnsureBookLoaded();
            if (_book == null) return 0;
            return _book.TryGetSideToMoveRank(state, out var rank) ? rank : 0;
        }

        private static bool ShouldPreferTieBreakMove(EightGameState state, EightMove candidate, EightMove currentBest)
        {
            // If scores are tied, prefer a move that lands exactly on 8 (hand gets removed).
            var candidateGetsEight = MoveGetsEight(state, candidate);
            var currentBestGetsEight = MoveGetsEight(state, currentBest);
            return candidateGetsEight && !currentBestGetsEight;
        }

        private static bool MoveGetsEight(EightGameState state, EightMove move)
        {
            int own;
            int opponent;

            if (state.WhiteTurn)
            {
                own = move.OwnHandIndex == 0 ? state.WhiteA : state.WhiteB;
                opponent = move.OpponentHandIndex == 0 ? state.BlackA : state.BlackB;
            }
            else
            {
                own = move.OwnHandIndex == 0 ? state.BlackA : state.BlackB;
                opponent = move.OpponentHandIndex == 0 ? state.WhiteA : state.WhiteB;
            }

            return own + opponent == 8;
        }
    }
}
