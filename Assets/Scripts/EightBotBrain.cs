using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts
{
    public class EightBotBrain : MonoBehaviour
    {
        [SerializeField] private TextAsset data8Json;

        private Data8Book _book;

        public EightGameState CurrentState => GameConfig.CurrentState;

        public void SetState(EightGameState state)
        {
            GameConfig.CurrentState = state;
        }

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
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = move;
                    foundByBook = true;
                }
            }

            if (useBookEdgeFilter && !foundByBook)
            {
                // If rule expansion and book transitions disagree, keep bot playable by falling back.
                foreach (var move in legalMoves)
                {
                    var child = EightGameRules.ApplyMove(state, move);
                    var score = GetOutcomeRankForPerspective(child, perspectiveIsWhite);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMove = move;
                    }
                }
            }

            return bestMove;
        }

        public StrategyEvaluation EvaluateCurrentState()
        {
            var state = GameConfig.CurrentState;
            var rank = GetOutcomeRankForPerspective(state, state.WhiteTurn);
            var outcome = ToStrategyOutcome(rank);
            var plies = Mathf.Abs(rank) >= 2 ? 2 : (rank == 0 ? 0 : 1);
            return new StrategyEvaluation(outcome, plies, GetOptimalMove());
        }

        public IReadOnlyList<EightMove> GetRuleBasedMoves()
        {
            return EightGameRules.GetLegalMoves(GameConfig.CurrentState);
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

        public IReadOnlyCollection<EightGameState> GetAllStatesFromData8()
        {
            EnsureBookLoaded();
            return _book?.States ?? new List<EightGameState>();
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
            if (!_book.TryGetClass(state, out var stateClass) || string.IsNullOrWhiteSpace(stateClass)) return 0;

            var tags = stateClass.Split(' ');
            var classSide = string.Empty;
            var hasWin = false;
            var hasWinWin = false;
            var hasLose = false;
            var hasLoseLose = false;

            foreach (var raw in tags)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var tag = raw.Trim().ToLowerInvariant();
                if (tag == "white" || tag == "black") classSide = tag;
                else if (tag == "win") hasWin = true;
                else if (tag == "winwin") hasWinWin = true;
                else if (tag == "lose") hasLose = true;
                else if (tag == "loselose") hasLoseLose = true;
            }

            var classSideRank = 0;
            if (hasWinWin) classSideRank = 2;
            else if (hasWin) classSideRank = 1;
            else if (hasLoseLose) classSideRank = -2;
            else if (hasLose) classSideRank = -1;

            if (string.IsNullOrEmpty(classSide) || classSideRank == 0) return 0;

            var sideToMoveToken = state.WhiteTurn ? "white" : "black";
            return classSide == sideToMoveToken ? classSideRank : -classSideRank;
        }

        private static StrategyOutcome ToStrategyOutcome(int rank)
        {
            if (rank > 0) return StrategyOutcome.Win;
            if (rank < 0) return StrategyOutcome.Lose;
            return StrategyOutcome.Draw;
        }
    }
}
