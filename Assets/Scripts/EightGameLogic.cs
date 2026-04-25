using System;
using System.Collections.Generic;
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
            var ownHands = state.WhiteTurn
                ? new[] { state.WhiteA, state.WhiteB }
                : new[] { state.BlackA, state.BlackB };
            var oppHands = state.WhiteTurn
                ? new[] { state.BlackA, state.BlackB }
                : new[] { state.WhiteA, state.WhiteB };

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
        private const int TurnBit = 1 << 12;

        private readonly Dictionary<int, EightGameState[]> _transitionsByKey = new Dictionary<int, EightGameState[]>();
        private readonly Dictionary<int, string> _classesByKey = new Dictionary<int, string>();
        private readonly Dictionary<int, sbyte> _rankForWhiteByKey = new Dictionary<int, sbyte>();
        private EightGameState[] _states = Array.Empty<EightGameState>();

        public IReadOnlyCollection<EightGameState> States => _states;

        public static Data8Book LoadFromText(string json)
        {
            var wrappedJson = "{\"items\":" + json + "}";
            var root = JsonUtility.FromJson<Data8Root>(wrappedJson);
            if (root == null || root.items == null)
            {
                throw new InvalidOperationException("Unable to parse data8.json.");
            }

            var book = new Data8Book();
            var nodeKeyById = new Dictionary<string, int>();
            var stateByKey = new Dictionary<int, EightGameState>();
            var transitionKeySets = new Dictionary<int, HashSet<int>>();

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
                    var stateKey = ToPackedStateKey(normalizedState);
                    nodeKeyById[item.data.id] = stateKey;
                    stateByKey[stateKey] = normalizedState;

                    var stateClass = item.classes ?? string.Empty;
                    book._classesByKey[stateKey] = stateClass;
                    book._rankForWhiteByKey[stateKey] = ParseRankForWhite(stateClass);

                    if (!transitionKeySets.ContainsKey(stateKey))
                    {
                        transitionKeySets[stateKey] = new HashSet<int>();
                    }
                }
            }

            book._states = new EightGameState[stateByKey.Count];
            var stateIndex = 0;
            foreach (var state in stateByKey.Values)
            {
                book._states[stateIndex++] = state;
            }

            foreach (var item in root.items)
            {
                if (item?.data == null || string.IsNullOrWhiteSpace(item.data.source) ||
                    string.IsNullOrWhiteSpace(item.data.target))
                {
                    continue;
                }

                if (!nodeKeyById.TryGetValue(item.data.source, out var sourceKey)) continue;
                if (!nodeKeyById.TryGetValue(item.data.target, out var targetKey)) continue;

                if (!transitionKeySets.TryGetValue(sourceKey, out var targetSet))
                {
                    targetSet = new HashSet<int>();
                    transitionKeySets[sourceKey] = targetSet;
                }

                targetSet.Add(targetKey);
            }

            foreach (var entry in transitionKeySets)
            {
                var nextStates = new EightGameState[entry.Value.Count];
                var i = 0;
                foreach (var targetKey in entry.Value)
                {
                    if (!stateByKey.TryGetValue(targetKey, out var targetState))
                    {
                        continue;
                    }

                    nextStates[i++] = targetState;
                }

                if (i != nextStates.Length)
                {
                    Array.Resize(ref nextStates, i);
                }

                book._transitionsByKey[entry.Key] = nextStates;
            }

            return book;
        }

        public bool TryGetClass(EightGameState state, out string stateClass)
        {
            var normalizedKey = ToPackedStateKey(state.NormalizeForCalculation());
            if (_classesByKey.TryGetValue(normalizedKey, out stateClass))
            {
                return true;
            }

            return _classesByKey.TryGetValue(FlipTurn(normalizedKey), out stateClass);
        }

        public bool TryGetSideToMoveRank(EightGameState state, out int rank)
        {
            var normalized = state.NormalizeForCalculation();
            var normalizedKey = ToPackedStateKey(normalized);
            if (_rankForWhiteByKey.TryGetValue(normalizedKey, out var rankForWhite))
            {
                rank = normalized.WhiteTurn ? rankForWhite : -rankForWhite;
                return true;
            }

            if (_rankForWhiteByKey.TryGetValue(FlipTurn(normalizedKey), out rankForWhite))
            {
                rank = normalized.WhiteTurn ? rankForWhite : -rankForWhite;
                return true;
            }

            rank = 0;
            return false;
        }

        public IReadOnlyList<EightGameState> GetPossibleNextStates(EightGameState state)
        {
            var normalizedKey = ToPackedStateKey(state.NormalizeForCalculation());
            if (_transitionsByKey.TryGetValue(normalizedKey, out var states))
            {
                return states;
            }

            if (_transitionsByKey.TryGetValue(FlipTurn(normalizedKey), out states))
            {
                return states;
            }

            return Array.Empty<EightGameState>();
        }

        private static int ToPackedStateKey(EightGameState state)
        {
            return state.WhiteA
                   | (state.WhiteB << 3)
                   | (state.BlackA << 6)
                   | (state.BlackB << 9)
                   | (state.WhiteTurn ? TurnBit : 0);
        }

        private static int FlipTurn(int key)
        {
            return key ^ TurnBit;
        }

        private static sbyte ParseRankForWhite(string stateClass)
        {
            if (string.IsNullOrWhiteSpace(stateClass))
            {
                return 0;
            }

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

            if (string.IsNullOrEmpty(classSide) || classSideRank == 0)
            {
                return 0;
            }

            return (sbyte)(classSide == "white" ? classSideRank : -classSideRank);
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
}
