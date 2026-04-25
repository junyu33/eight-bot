using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Assets.Scripts
{
    [Serializable]
    public readonly struct EightGameState : IEquatable<EightGameState>
    {
        public readonly int WhiteA;
        public readonly int WhiteB;
        public readonly int BlackA;
        public readonly int BlackB;
        public readonly bool WhiteTurn;

        public EightGameState(int whiteA, int whiteB, int blackA, int blackB, bool whiteTurn)
        {
            WhiteA = Mathf.Clamp(whiteA, 0, 7);
            WhiteB = Mathf.Clamp(whiteB, 0, 7);
            BlackA = Mathf.Clamp(blackA, 0, 7);
            BlackB = Mathf.Clamp(blackB, 0, 7);
            WhiteTurn = whiteTurn;
        }

        public static EightGameState CreateStart(bool playerFirstAsWhite)
        {
            return new EightGameState(1, 1, 1, 1, playerFirstAsWhite);
        }

        public bool IsTerminal
        {
            get
            {
                var whiteDead = WhiteA == 0 && WhiteB == 0;
                var blackDead = BlackA == 0 && BlackB == 0;
                return whiteDead || blackDead;
            }
        }

        public int OutcomeForSideToMove
        {
            get
            {
                var sideToMoveDead = WhiteTurn
                    ? WhiteA == 0 && WhiteB == 0
                    : BlackA == 0 && BlackB == 0;

                var opponentDead = WhiteTurn
                    ? BlackA == 0 && BlackB == 0
                    : WhiteA == 0 && WhiteB == 0;

                if (sideToMoveDead && opponentDead) return 0;
                if (sideToMoveDead) return -1;
                if (opponentDead) return 1;
                return 0;
            }
        }

        public bool Equals(EightGameState other)
        {
            return WhiteA == other.WhiteA
                   && WhiteB == other.WhiteB
                   && BlackA == other.BlackA
                   && BlackB == other.BlackB
                   && WhiteTurn == other.WhiteTurn;
        }

        public override bool Equals(object obj)
        {
            return obj is EightGameState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = WhiteA;
                hashCode = (hashCode * 397) ^ WhiteB;
                hashCode = (hashCode * 397) ^ BlackA;
                hashCode = (hashCode * 397) ^ BlackB;
                hashCode = (hashCode * 397) ^ (WhiteTurn ? 1 : 0);
                return hashCode;
            }
        }

        public override string ToString()
        {
            return WhiteTurn
                ? $"({WhiteA}, {WhiteB}) < | ({BlackA}, {BlackB})"
                : $"({WhiteA}, {WhiteB}) | ({BlackA}, {BlackB}) <";
        }

        public EightGameState NormalizeForCalculation()
        {
            NormalizePair(WhiteA, WhiteB, out var whiteA, out var whiteB);
            NormalizePair(BlackA, BlackB, out var blackA, out var blackB);
            return new EightGameState(whiteA, whiteB, blackA, blackB, WhiteTurn);
        }

        private static void NormalizePair(int first, int second, out int a, out int b)
        {
            if (GetNormalizationOrder(first) <= GetNormalizationOrder(second))
            {
                a = first;
                b = second;
                return;
            }

            a = second;
            b = first;
        }

        // data8 canonical form treats "None" as the largest hand value when ordering pairs.
        private static int GetNormalizationOrder(int value)
        {
            return value == 0 ? 8 : value;
        }
    }

    public readonly struct EightMove : IEquatable<EightMove>
    {
        public readonly int OwnHandIndex;
        public readonly int OpponentHandIndex;

        public EightMove(int ownHandIndex, int opponentHandIndex)
        {
            OwnHandIndex = ownHandIndex;
            OpponentHandIndex = opponentHandIndex;
        }

        public bool Equals(EightMove other)
        {
            return OwnHandIndex == other.OwnHandIndex && OpponentHandIndex == other.OpponentHandIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is EightMove other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (OwnHandIndex * 397) ^ OpponentHandIndex;
            }
        }

        public override string ToString()
        {
            return $"own[{OwnHandIndex}] += opp[{OpponentHandIndex}]";
        }
    }

    public static class EightGameRules
    {
        public static IReadOnlyList<EightMove> GetLegalMoves(EightGameState state)
        {
            var ownHands = state.WhiteTurn ? new[] { state.WhiteA, state.WhiteB } : new[] { state.BlackA, state.BlackB };
            var oppHands = state.WhiteTurn ? new[] { state.BlackA, state.BlackB } : new[] { state.WhiteA, state.WhiteB };

            var moves = new List<EightMove>(4);
            for (var ownIndex = 0; ownIndex < ownHands.Length; ownIndex++)
            {
                if (ownHands[ownIndex] == 0) continue;

                for (var oppIndex = 0; oppIndex < oppHands.Length; oppIndex++)
                {
                    if (oppHands[oppIndex] == 0) continue;
                    moves.Add(new EightMove(ownIndex, oppIndex));
                }
            }

            return moves;
        }

        public static EightGameState ApplyMove(EightGameState state, EightMove move)
        {
            var white = new[] { state.WhiteA, state.WhiteB };
            var black = new[] { state.BlackA, state.BlackB };

            if (state.WhiteTurn)
            {
                if (white[move.OwnHandIndex] == 0 || black[move.OpponentHandIndex] == 0)
                {
                    throw new InvalidOperationException("Illegal move for current state.");
                }

                white[move.OwnHandIndex] = NextHandValue(white[move.OwnHandIndex], black[move.OpponentHandIndex]);
            }
            else
            {
                if (black[move.OwnHandIndex] == 0 || white[move.OpponentHandIndex] == 0)
                {
                    throw new InvalidOperationException("Illegal move for current state.");
                }

                black[move.OwnHandIndex] = NextHandValue(black[move.OwnHandIndex], white[move.OpponentHandIndex]);
            }

            return new EightGameState(white[0], white[1], black[0], black[1], !state.WhiteTurn);
        }

        private static int NextHandValue(int own, int opponent)
        {
            var sum = own + opponent;
            if (sum == 8) return 0;
            if (sum > 7) return 1;
            return sum;
        }
    }

    public sealed class Data8Book
    {
        private readonly Dictionary<EightGameState, HashSet<EightGameState>> _transitions = new Dictionary<EightGameState, HashSet<EightGameState>>();
        private readonly Dictionary<EightGameState, string> _classesByState = new Dictionary<EightGameState, string>();

        public IReadOnlyCollection<EightGameState> States => _classesByState.Keys.ToArray();

        public static Data8Book LoadFromText(string json)
        {
            var wrappedJson = "{\"items\":" + json + "}";
            var root = JsonUtility.FromJson<Data8Root>(wrappedJson);
            if (root == null || root.items == null)
            {
                throw new InvalidOperationException("Unable to parse data8.json.");
            }

            var book = new Data8Book();
            var nodeById = new Dictionary<string, EightGameState>();

            foreach (var item in root.items)
            {
                if (item?.data == null || string.IsNullOrWhiteSpace(item.data.id))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.data.source) && string.IsNullOrWhiteSpace(item.data.target))
                {
                    if (!TryParseState(item.data.label, out var state))
                    {
                        continue;
                    }

                    var normalizedState = state.NormalizeForCalculation();
                    nodeById[item.data.id] = normalizedState;
                    book._classesByState[normalizedState] = item.classes ?? string.Empty;
                    if (!book._transitions.ContainsKey(normalizedState))
                    {
                        book._transitions[normalizedState] = new HashSet<EightGameState>();
                    }
                }
            }

            foreach (var item in root.items)
            {
                if (item?.data == null || string.IsNullOrWhiteSpace(item.data.source) || string.IsNullOrWhiteSpace(item.data.target))
                {
                    continue;
                }

                if (!nodeById.TryGetValue(item.data.source, out var sourceState)) continue;
                if (!nodeById.TryGetValue(item.data.target, out var targetState)) continue;

                if (!book._transitions.TryGetValue(sourceState, out var targets))
                {
                    targets = new HashSet<EightGameState>();
                    book._transitions[sourceState] = targets;
                }

                targets.Add(targetState);
            }

            return book;
        }

        public bool TryGetClass(EightGameState state, out string stateClass)
        {
            var normalized = state.NormalizeForCalculation();
            if (_classesByState.TryGetValue(normalized, out stateClass))
            {
                return true;
            }

            var flippedTurn = new EightGameState(
                normalized.WhiteA,
                normalized.WhiteB,
                normalized.BlackA,
                normalized.BlackB,
                !normalized.WhiteTurn);

            return _classesByState.TryGetValue(flippedTurn, out stateClass);
        }

        public IReadOnlyList<EightGameState> GetPossibleNextStates(EightGameState state)
        {
            var normalized = state.NormalizeForCalculation();
            if (_transitions.TryGetValue(normalized, out var states))
            {
                return states.ToArray();
            }

            var flippedTurn = new EightGameState(
                normalized.WhiteA,
                normalized.WhiteB,
                normalized.BlackA,
                normalized.BlackB,
                !normalized.WhiteTurn);
            if (_transitions.TryGetValue(flippedTurn, out states))
            {
                return states.ToArray();
            }

            return Array.Empty<EightGameState>();
        }

        private static bool TryParseState(string label, out EightGameState state)
        {
            state = default;
            if (string.IsNullOrWhiteSpace(label)) return false;
            var normalized = label.Replace("\r", string.Empty);
            var lines = normalized.Split('\n');
            if (lines.Length != 2) return false;

            if (!TryParsePairLine(lines[0], out var whiteA, out var whiteB, out var whiteMarker)) return false;
            if (!TryParsePairLine(lines[1], out var blackA, out var blackB, out var blackMarker)) return false;

            var whiteTurn = whiteMarker && !blackMarker;
            var blackTurn = blackMarker && !whiteMarker;
            if (!whiteTurn && !blackTurn) return false;

            state = new EightGameState(whiteA, whiteB, blackA, blackB, whiteTurn);
            return true;
        }

        private static bool TryParsePairLine(string line, out int a, out int b, out bool hasTurnMarker)
        {
            a = 0;
            b = 0;
            hasTurnMarker = false;
            if (line == null) return false;

            var open = line.IndexOf('(');
            var comma = line.IndexOf(',', open + 1);
            var close = line.IndexOf(')', comma + 1);
            if (open < 0 || comma < 0 || close < 0) return false;

            var aToken = line.Substring(open + 1, comma - open - 1);
            var bToken = line.Substring(comma + 1, close - comma - 1);
            if (!TryParseHandToken(aToken, out a)) return false;
            if (!TryParseHandToken(bToken, out b)) return false;

            hasTurnMarker = line.IndexOf('<', close + 1) >= 0;
            return true;
        }

        private static bool TryParseHandToken(string token, out int value)
        {
            token = token.Trim();
            if (token.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                value = 0;
                return true;
            }

            return int.TryParse(token, out value);
        }

        [Serializable]
        private sealed class Data8Root
        {
            public Data8Item[] items;
        }

        [Serializable]
        private sealed class Data8Item
        {
            public Data8Data data;
            public string classes;
        }

        [Serializable]
        private sealed class Data8Data
        {
            public string id;
            public string label;
            public string source;
            public string target;
        }
    }

    public enum StrategyOutcome
    {
        Lose = -1,
        Draw = 0,
        Win = 1
    }

    public readonly struct StrategyEvaluation
    {
        public readonly StrategyOutcome Outcome;
        public readonly int Plies;
        public readonly EightMove? BestMove;

        public StrategyEvaluation(StrategyOutcome outcome, int plies, EightMove? bestMove)
        {
            Outcome = outcome;
            Plies = plies;
            BestMove = bestMove;
        }
    }

    public sealed class EightBotSolver
    {
        private readonly Dictionary<EightGameState, StrategyScore> _memo = new Dictionary<EightGameState, StrategyScore>();
        private readonly HashSet<EightGameState> _active = new HashSet<EightGameState>();

        public StrategyEvaluation Evaluate(EightGameState state)
        {
            if (state.IsTerminal)
            {
                var terminalOutcome = ToOutcome(state.OutcomeForSideToMove);
                return new StrategyEvaluation(terminalOutcome, 0, null);
            }

            var legalMoves = EightGameRules.GetLegalMoves(state);
            if (legalMoves.Count == 0)
            {
                return new StrategyEvaluation(StrategyOutcome.Lose, 0, null);
            }

            var best = new StrategyEvaluation(StrategyOutcome.Lose, int.MinValue, null);
            foreach (var move in legalMoves)
            {
                var child = EightGameRules.ApplyMove(state, move);
                var childScore = EvaluateScoreInternal(child);
                var candidate = FlipPerspective(new StrategyEvaluation(childScore.Outcome, childScore.Plies, null), move);
                if (IsBetter(candidate, best))
                {
                    best = candidate;
                }
            }

            return best;
        }

        public EightMove? GetBestMove(EightGameState state)
        {
            return Evaluate(state).BestMove;
        }

        private StrategyScore EvaluateScoreInternal(EightGameState state)
        {
            var key = state.NormalizeForCalculation();
            if (_memo.TryGetValue(key, out var cached)) return cached;

            if (state.IsTerminal)
            {
                var terminal = new StrategyScore(ToOutcome(state.OutcomeForSideToMove), 0);
                _memo[key] = terminal;
                return terminal;
            }

            if (_active.Contains(key))
            {
                return new StrategyScore(StrategyOutcome.Draw, 0);
            }

            _active.Add(key);
            try
            {
                var legalMoves = EightGameRules.GetLegalMoves(state);
                if (legalMoves.Count == 0)
                {
                    var noMove = new StrategyScore(StrategyOutcome.Lose, 0);
                    _memo[key] = noMove;
                    return noMove;
                }

                var best = new StrategyScore(StrategyOutcome.Lose, int.MinValue);

                foreach (var move in legalMoves)
                {
                    var child = EightGameRules.ApplyMove(state, move);
                    var childEval = EvaluateScoreInternal(child);
                    var currentEval = FlipPerspectiveScore(childEval);

                    if (IsBetterScore(currentEval, best))
                    {
                        best = currentEval;
                    }
                }

                _memo[key] = best;
                return best;
            }
            finally
            {
                _active.Remove(key);
            }
        }

        private static StrategyScore FlipPerspectiveScore(StrategyScore child)
        {
            switch (child.Outcome)
            {
                case StrategyOutcome.Win:
                    return new StrategyScore(StrategyOutcome.Lose, child.Plies + 1);
                case StrategyOutcome.Lose:
                    return new StrategyScore(StrategyOutcome.Win, child.Plies + 1);
                default:
                    return new StrategyScore(StrategyOutcome.Draw, child.Plies + 1);
            }
        }

        private static bool IsBetterScore(StrategyScore candidate, StrategyScore currentBest)
        {
            if (candidate.Outcome != currentBest.Outcome)
            {
                return candidate.Outcome > currentBest.Outcome;
            }

            if (candidate.Outcome == StrategyOutcome.Win)
            {
                return candidate.Plies < currentBest.Plies;
            }

            if (candidate.Outcome == StrategyOutcome.Lose)
            {
                return candidate.Plies > currentBest.Plies;
            }

            return candidate.Plies < currentBest.Plies;
        }

        private static StrategyEvaluation FlipPerspective(StrategyEvaluation child, EightMove moveUsed)
        {
            switch (child.Outcome)
            {
                case StrategyOutcome.Win:
                    return new StrategyEvaluation(StrategyOutcome.Lose, child.Plies + 1, moveUsed);
                case StrategyOutcome.Lose:
                    return new StrategyEvaluation(StrategyOutcome.Win, child.Plies + 1, moveUsed);
                default:
                    return new StrategyEvaluation(StrategyOutcome.Draw, child.Plies + 1, moveUsed);
            }
        }

        private static bool IsBetter(StrategyEvaluation candidate, StrategyEvaluation currentBest)
        {
            if (candidate.Outcome != currentBest.Outcome)
            {
                return candidate.Outcome > currentBest.Outcome;
            }

            if (candidate.Outcome == StrategyOutcome.Win)
            {
                return candidate.Plies < currentBest.Plies;
            }

            if (candidate.Outcome == StrategyOutcome.Lose)
            {
                return candidate.Plies > currentBest.Plies;
            }

            return candidate.Plies < currentBest.Plies;
        }

        private readonly struct StrategyScore
        {
            public readonly StrategyOutcome Outcome;
            public readonly int Plies;

            public StrategyScore(StrategyOutcome outcome, int plies)
            {
                Outcome = outcome;
                Plies = plies;
            }
        }

        private static StrategyOutcome ToOutcome(int value)
        {
            if (value > 0) return StrategyOutcome.Win;
            if (value < 0) return StrategyOutcome.Lose;
            return StrategyOutcome.Draw;
        }
    }
}
