using UnityEngine;
using System.Linq;
using System.Collections;
using UnityEngine.InputSystem;

namespace Assets.Scripts
{
    public class EightGamePlayableController : MonoBehaviour
    {
        [SerializeField] private EightBotBrain brain;
        [SerializeField] private bool whiteIsHuman = true;
        [SerializeField] private bool blackIsHuman = false;
        [SerializeField] private float botMoveDelaySeconds = 0.55f;
        [Header("Bot Move Animation")] [SerializeField]
        private float botApproachDurationSeconds = 0.18f;
        [SerializeField] private float botStopNearTargetDistance = 0.8f;

        [SerializeField] private float botImpactPauseSeconds = 0.1f;
        [SerializeField] private float botResultHoldSeconds = 0.2f;
        [SerializeField] private float botReturnDurationSeconds = 0.2f;
        [Header("Debug UI")] [SerializeField] private bool showDebugPanel = false;
        [SerializeField] private bool allowToggleDebugHotkey = true;
        [SerializeField] private Key toggleDebugKey = Key.F1;
        [Header("Turn Background")] [SerializeField]
        private Color p1TurnBackgroundColor = new Color(0.06f, 0.32f, 0.73f, 1f); // sapphire

        [SerializeField] private Color p2TurnBackgroundColor = new Color(0.98f, 0.62f, 0.82f, 1f); // pink

        [Header("Drag Input")] [SerializeField]
        private Transform p1Left;

        [SerializeField] private Transform p1Right;
        [SerializeField] private Transform p2Left;
        [SerializeField] private Transform p2Right;
        [SerializeField] private float pickRadiusWorld = 1.4f;
        [SerializeField] private float dropNearOpponentDistance = 2.1f;

        [Header("Hand Sprites (1..7)")] [SerializeField]
        private Sprite[] blueHandSprites = new Sprite[7];

        [SerializeField] private Sprite[] redHandSprites = new Sprite[7];
        [SerializeField] private int spriteSortingOrder = 10;

        private float _nextBotMoveAt;
        private Camera _mainCamera;
        private Vector3 _p1LeftStart;
        private Vector3 _p1RightStart;
        private Vector3 _p2LeftStart;
        private Vector3 _p2RightStart;
        private SpriteRenderer _p1LeftRenderer;
        private SpriteRenderer _p1RightRenderer;
        private SpriteRenderer _p2LeftRenderer;
        private SpriteRenderer _p2RightRenderer;
        private bool _isDragging;
        private bool _draggingWhite;
        private int _dragOwnHandIndex = -1;
        private bool _isBotAnimating;
        private bool _botAnimatingTurnWasWhite;
        private bool _isPlayerReturnAnimating;

        private void Start()
        {
            if (brain == null)
            {
                brain = GetComponent<EightBotBrain>();
            }

            BindHandTransformsIfNeeded();
            CacheHandStartPositions();
            EnsureHandRenderers();
            AutoFillSpritesFromProjectIfNeeded();
            _mainCamera = Camera.main;

            GameConfig.ResetState();
            ApplyTurnBackgroundColor(GameConfig.CurrentState);
            RefreshHandSprites();
            _nextBotMoveAt = Time.time + botMoveDelaySeconds;
        }

        private void Update()
        {
            if (brain == null) return;

            var state = brain.CurrentState;
            if (state.IsTerminal)
            {
                if (!_isBotAnimating && !_isPlayerReturnAnimating)
                {
                    ResetHandPositions();
                }

                RefreshHandSprites();
                if (_isBotAnimating)
                {
                    ApplyTurnBackgroundColor(_botAnimatingTurnWasWhite);
                }
                else
                {
                    ApplyTurnBackgroundColor(state);
                }

                return;
            }

            HandleDebugPanelToggleHotkey();

            if (_isBotAnimating || _isPlayerReturnAnimating)
            {
                RefreshHandSprites();
                if (_isBotAnimating)
                {
                    ApplyTurnBackgroundColor(_botAnimatingTurnWasWhite);
                }
                else
                {
                    ApplyTurnBackgroundColor(GameConfig.CurrentState);
                }

                return;
            }

            HandleHumanDragInput(state);
            RefreshHandSprites();
            ApplyTurnBackgroundColor(GameConfig.CurrentState);

            if (IsHumanTurn(state)) return;
            if (Time.time < _nextBotMoveAt) return;

            var move = brain.GetOptimalMove();
            if (move.HasValue)
            {
                StartCoroutine(PlayBotMoveAnimationAndApply(state, move.Value));
            }
            else
            {
                _nextBotMoveAt = Time.time + botMoveDelaySeconds;
            }
        }

        private void OnGUI()
        {
            DrawEndGameBanner();

            if (!showDebugPanel) return;

            if (brain == null)
            {
                DrawMissingBrainMessage();
                return;
            }

            var state = brain.CurrentState;
            var eval = brain.EvaluateCurrentState();
            brain.TryGetCurrentStateClassFromData8(out var data8Class);
            var nextCount = brain.GetAllPossibleNextStatesFromData8().Count;

            GUILayout.BeginArea(new Rect(20f, 20f, 560f, 560f), GUI.skin.box);
            GUILayout.Label("Rule Of 8 - Playable Controller");
            GUILayout.Space(6f);
            GUILayout.Label($"White: ({state.WhiteA}, {state.WhiteB})");
            GUILayout.Label($"Black: ({state.BlackA}, {state.BlackB})");
            GUILayout.Label($"Turn: {(state.WhiteTurn ? "White" : "Black")}");
            GUILayout.Label($"Solver: {eval.Outcome} in {eval.Plies} ply");
            GUILayout.Label($"data8 class: {(string.IsNullOrWhiteSpace(data8Class) ? "<none>" : data8Class)}");
            GUILayout.Label($"data8 next states: {nextCount}");

            if (state.IsTerminal)
            {
                GUILayout.Space(10f);
                GUILayout.Label(GetWinnerText(state));
                if (GUILayout.Button("Restart", GUILayout.Height(32f)))
                {
                    RestartGame();
                }

                GUILayout.EndArea();
                return;
            }

            GUILayout.Space(10f);
            if (IsHumanTurn(state))
            {
                GUILayout.Label("Human turn: drag your hand close to one opponent hand and release.");
            }
            else
            {
                GUILayout.Label("Bot is thinking...");
            }

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Restart", GUILayout.Height(28f)))
            {
                RestartGame();
            }

            if (GUILayout.Button("Toggle White Human", GUILayout.Height(28f)))
            {
                whiteIsHuman = !whiteIsHuman;
            }

            if (GUILayout.Button("Toggle Black Human", GUILayout.Height(28f)))
            {
                blackIsHuman = !blackIsHuman;
            }

            GUILayout.EndHorizontal();

            GUILayout.Label($"White Human: {whiteIsHuman}, Black Human: {blackIsHuman}");
            GUILayout.EndArea();
        }

        private void DrawEndGameBanner()
        {
            if (brain == null) return;

            var state = brain.CurrentState;
            if (!state.IsTerminal) return;

            var whiteDead = state.WhiteA == 0 && state.WhiteB == 0;
            var blackDead = state.BlackA == 0 && state.BlackB == 0;

            string text;
            if (whiteDead && blackDead) text = "Draw!";
            else if (whiteDead) text = "Red wins!";
            else if (blackDead) text = "Blue wins!";
            else text = "Game over";

            var oldAlignment = GUI.skin.label.alignment;
            var oldFontSize = GUI.skin.label.fontSize;
            var oldTextColor = GUI.skin.label.normal.textColor;

            GUI.skin.label.alignment = TextAnchor.MiddleCenter;
            GUI.skin.label.fontSize = 48;
            GUI.skin.label.normal.textColor = Color.white;

            const float width = 520f;
            const float height = 90f;
            var x = (Screen.width - width) * 0.5f;
            var y = (Screen.height - height) * 0.5f - 20f;

            GUI.Box(new Rect(x - 12f, y - 8f, width + 24f, height + 16f), GUIContent.none);
            GUI.Label(new Rect(x, y, width, height), text);

            GUI.skin.label.alignment = oldAlignment;
            GUI.skin.label.fontSize = oldFontSize;
            GUI.skin.label.normal.textColor = oldTextColor;
        }

        private bool IsHumanTurn(EightGameState state)
        {
            return state.WhiteTurn ? whiteIsHuman : blackIsHuman;
        }

        private static string GetWinnerText(EightGameState state)
        {
            var whiteDead = state.WhiteA == 0 && state.WhiteB == 0;
            var blackDead = state.BlackA == 0 && state.BlackB == 0;

            if (whiteDead && blackDead) return "Game over: Draw";
            if (whiteDead) return "Game over: Black wins";
            if (blackDead) return "Game over: White wins";
            return "Game over";
        }

        private void HandleHumanDragInput(EightGameState state)
        {
            if (!IsHumanTurn(state))
            {
                _isDragging = false;
                _dragOwnHandIndex = -1;
                ResetHandPositions();
                return;
            }

            if (_mainCamera == null) _mainCamera = Camera.main;
            ResetHandPositionsExceptDragging();

            if (TryGetPointerDownScreen(out var downScreen))
            {
                var downPos = ScreenToWorldOnHandsPlane(downScreen);
                TryStartDrag(state, downPos);
            }

            if (_isDragging && TryGetPointerHeldScreen(out var heldScreen))
            {
                var heldPos = ScreenToWorldOnHandsPlane(heldScreen);
                var dragTransform = GetHandTransform(_draggingWhite, _dragOwnHandIndex);
                if (dragTransform != null)
                {
                    var p = heldPos;
                    p.z = dragTransform.position.z;
                    dragTransform.position = p;
                }
            }

            if (_isDragging && TryGetPointerUpScreen(out var upScreen))
            {
                var upPos = ScreenToWorldOnHandsPlane(upScreen);
                var moveApplied = TryFinishDrag(state, upPos);
                _isDragging = false;
                _dragOwnHandIndex = -1;
                if (!moveApplied)
                {
                    ResetHandPositions();
                }
            }
        }

        private void TryStartDrag(EightGameState state, Vector3 pointerWorld)
        {
            var ownIsWhite = state.WhiteTurn;
            var ownAValue = ownIsWhite ? state.WhiteA : state.BlackA;
            var ownBValue = ownIsWhite ? state.WhiteB : state.BlackB;

            var a = GetHandTransform(ownIsWhite, 0);
            var b = GetHandTransform(ownIsWhite, 1);
            if (a == null || b == null) return;

            var chosenIndex = -1;
            var bestDistance = float.MaxValue;

            if (ownAValue > 0)
            {
                var d = Vector2.Distance(pointerWorld, a.position);
                if (d <= pickRadiusWorld && d < bestDistance)
                {
                    chosenIndex = 0;
                    bestDistance = d;
                }
            }

            if (ownBValue > 0)
            {
                var d = Vector2.Distance(pointerWorld, b.position);
                if (d <= pickRadiusWorld && d < bestDistance)
                {
                    chosenIndex = 1;
                }
            }

            if (chosenIndex < 0) return;

            _isDragging = true;
            _draggingWhite = ownIsWhite;
            _dragOwnHandIndex = chosenIndex;
        }

        private bool TryFinishDrag(EightGameState state, Vector3 pointerWorld)
        {
            var ownIsWhite = state.WhiteTurn;
            if (!_isDragging || ownIsWhite != _draggingWhite) return false;

            var ownValue = ownIsWhite
                ? (_dragOwnHandIndex == 0 ? state.WhiteA : state.WhiteB)
                : (_dragOwnHandIndex == 0 ? state.BlackA : state.BlackB);
            if (ownValue == 0) return false;

            var oppAValue = ownIsWhite ? state.BlackA : state.WhiteA;
            var oppBValue = ownIsWhite ? state.BlackB : state.WhiteB;

            var oppA = GetHandTransform(!ownIsWhite, 0);
            var oppB = GetHandTransform(!ownIsWhite, 1);
            if (oppA == null || oppB == null) return false;

            var chosenOpponentIndex = -1;
            var bestDistance = float.MaxValue;

            if (oppAValue > 0)
            {
                var d = Vector2.Distance(pointerWorld, oppA.position);
                if (d <= dropNearOpponentDistance && d < bestDistance)
                {
                    chosenOpponentIndex = 0;
                    bestDistance = d;
                }
            }

            if (oppBValue > 0)
            {
                var d = Vector2.Distance(pointerWorld, oppB.position);
                if (d <= dropNearOpponentDistance && d < bestDistance)
                {
                    chosenOpponentIndex = 1;
                }
            }

            if (chosenOpponentIndex < 0) return false;

            var ownHand = GetHandTransform(ownIsWhite, _dragOwnHandIndex);
            var ownStartPos = GetHandStartPosition(ownIsWhite, _dragOwnHandIndex);
            brain.ApplyMove(new EightMove(_dragOwnHandIndex, chosenOpponentIndex));
            RefreshHandSprites();

            if (ownHand != null)
            {
                StartCoroutine(AnimatePlayerHandBack(ownHand, ownHand.position, ownStartPos));
            }
            else
            {
                ResetHandPositions();
                _nextBotMoveAt = Time.time + botMoveDelaySeconds;
            }

            return true;
        }

        private IEnumerator PlayBotMoveAnimationAndApply(EightGameState stateBeforeMove, EightMove move)
        {
            _isBotAnimating = true;
            _botAnimatingTurnWasWhite = stateBeforeMove.WhiteTurn;

            var ownIsWhite = stateBeforeMove.WhiteTurn;
            var ownHand = GetHandTransform(ownIsWhite, move.OwnHandIndex);
            var targetHand = GetHandTransform(!ownIsWhite, move.OpponentHandIndex);

            if (ownHand == null || targetHand == null)
            {
                brain.ApplyMove(move);
                ResetHandPositions();
                _nextBotMoveAt = Time.time + botMoveDelaySeconds;
                _isBotAnimating = false;
                yield break;
            }

            var startPos = ownHand.position;
            var targetPos = targetHand.position;
            targetPos.z = startPos.z;
            var toTarget = targetPos - startPos;
            var stopPos = targetPos;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                var distance = toTarget.magnitude;
                var travel = Mathf.Max(0f, distance - Mathf.Max(0f, botStopNearTargetDistance));
                stopPos = startPos + toTarget.normalized * travel;
            }

            yield return AnimateHandPosition(ownHand, startPos, stopPos, botApproachDurationSeconds);

            // Become the sum immediately on contact, then hold before returning.
            brain.ApplyMove(move);
            RefreshHandSprites();

            if (botImpactPauseSeconds > 0f)
            {
                yield return new WaitForSeconds(botImpactPauseSeconds);
            }

            if (botResultHoldSeconds > 0f)
            {
                yield return new WaitForSeconds(botResultHoldSeconds);
            }

            yield return AnimateHandPosition(ownHand, ownHand.position, startPos, botReturnDurationSeconds);

            ResetHandPositions();
            _nextBotMoveAt = Time.time + botMoveDelaySeconds;
            _isBotAnimating = false;
        }

        private IEnumerator AnimatePlayerHandBack(Transform hand, Vector3 from, Vector3 to)
        {
            _isPlayerReturnAnimating = true;

            if (botResultHoldSeconds > 0f)
            {
                yield return new WaitForSeconds(botResultHoldSeconds);
            }

            yield return AnimateHandPosition(hand, from, to, botReturnDurationSeconds);
            ResetHandPositions();
            _isPlayerReturnAnimating = false;
            _nextBotMoveAt = Time.time + botMoveDelaySeconds;
        }

        private static IEnumerator AnimateHandPosition(Transform hand, Vector3 from, Vector3 to, float durationSeconds)
        {
            if (hand == null) yield break;

            if (durationSeconds <= 0f)
            {
                hand.position = to;
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < durationSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / durationSeconds);
                var easedT = Mathf.SmoothStep(0f, 1f, t);
                hand.position = Vector3.Lerp(from, to, easedT);
                yield return null;
            }

            hand.position = to;
        }

        private Transform GetHandTransform(bool white, int handIndex)
        {
            if (white)
            {
                return handIndex == 0 ? p1Left : p1Right;
            }

            return handIndex == 0 ? p2Left : p2Right;
        }

        private void BindHandTransformsIfNeeded()
        {
            if (p1Left == null)
            {
                var go = GameObject.Find("P1Left");
                if (go != null) p1Left = go.transform;
            }

            if (p1Right == null)
            {
                var go = GameObject.Find("P1Right");
                if (go != null) p1Right = go.transform;
            }

            if (p2Left == null)
            {
                var go = GameObject.Find("P2Left");
                if (go != null) p2Left = go.transform;
            }

            if (p2Right == null)
            {
                var go = GameObject.Find("P2Right");
                if (go != null) p2Right = go.transform;
            }
        }

        private void CacheHandStartPositions()
        {
            if (p1Left != null) _p1LeftStart = p1Left.position;
            if (p1Right != null) _p1RightStart = p1Right.position;
            if (p2Left != null) _p2LeftStart = p2Left.position;
            if (p2Right != null) _p2RightStart = p2Right.position;
        }

        private Vector3 GetHandStartPosition(bool white, int handIndex)
        {
            if (white)
            {
                return handIndex == 0 ? _p1LeftStart : _p1RightStart;
            }

            return handIndex == 0 ? _p2LeftStart : _p2RightStart;
        }

        private void ResetHandPositions()
        {
            if (p1Left != null) p1Left.position = _p1LeftStart;
            if (p1Right != null) p1Right.position = _p1RightStart;
            if (p2Left != null) p2Left.position = _p2LeftStart;
            if (p2Right != null) p2Right.position = _p2RightStart;
        }

        private void ResetHandPositionsExceptDragging()
        {
            if (!_isDragging)
            {
                ResetHandPositions();
                return;
            }

            if (p1Left != null && !(_draggingWhite && _dragOwnHandIndex == 0)) p1Left.position = _p1LeftStart;
            if (p1Right != null && !(_draggingWhite && _dragOwnHandIndex == 1)) p1Right.position = _p1RightStart;
            if (p2Left != null && !(!_draggingWhite && _dragOwnHandIndex == 0)) p2Left.position = _p2LeftStart;
            if (p2Right != null && !(!_draggingWhite && _dragOwnHandIndex == 1)) p2Right.position = _p2RightStart;
        }

        private static bool TryGetPointerDownScreen(out Vector3 screen)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
            {
                var p = touchscreen.primaryTouch.position.ReadValue();
                screen = new Vector3(p.x, p.y, 0f);
                return true;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                var p = mouse.position.ReadValue();
                screen = new Vector3(p.x, p.y, 0f);
                return true;
            }

            screen = default;
            return false;
        }

        private static bool TryGetPointerHeldScreen(out Vector3 screen)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.isPressed)
            {
                var p = touchscreen.primaryTouch.position.ReadValue();
                screen = new Vector3(p.x, p.y, 0f);
                return true;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                var p = mouse.position.ReadValue();
                screen = new Vector3(p.x, p.y, 0f);
                return true;
            }

            screen = default;
            return false;
        }

        private static bool TryGetPointerUpScreen(out Vector3 screen)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.wasReleasedThisFrame)
            {
                var p = touchscreen.primaryTouch.position.ReadValue();
                screen = new Vector3(p.x, p.y, 0f);
                return true;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasReleasedThisFrame)
            {
                var p = mouse.position.ReadValue();
                screen = new Vector3(p.x, p.y, 0f);
                return true;
            }

            screen = default;
            return false;
        }

        private Vector3 ScreenToWorldOnHandsPlane(Vector3 pointerScreen)
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return Vector3.zero;

            var zPlane = 0f;
            if (p1Left != null) zPlane = p1Left.position.z;
            else if (p1Right != null) zPlane = p1Right.position.z;

            pointerScreen.z = Mathf.Abs(_mainCamera.transform.position.z - zPlane);
            var world = _mainCamera.ScreenToWorldPoint(pointerScreen);
            world.z = zPlane;
            return world;
        }

        private void RestartGame()
        {
            GameConfig.ResetState();
            _isDragging = false;
            _dragOwnHandIndex = -1;
            ResetHandPositions();
            RefreshHandSprites();
            ApplyTurnBackgroundColor(GameConfig.CurrentState);
            _nextBotMoveAt = Time.time + botMoveDelaySeconds;
        }

        private void HandleDebugPanelToggleHotkey()
        {
            if (!allowToggleDebugHotkey) return;
            if (Keyboard.current == null) return;
            if (toggleDebugKey == Key.None) return;

            var keyControl = Keyboard.current[toggleDebugKey];
            if (keyControl != null && keyControl.wasPressedThisFrame)
            {
                showDebugPanel = !showDebugPanel;
            }
        }

        private void ApplyTurnBackgroundColor(EightGameState state)
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            ApplyTurnBackgroundColor(state.WhiteTurn);
        }

        private void ApplyTurnBackgroundColor(bool whiteTurn)
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            _mainCamera.backgroundColor = whiteTurn ? p1TurnBackgroundColor : p2TurnBackgroundColor;
        }

        private void EnsureHandRenderers()
        {
            _p1LeftRenderer = EnsureRenderer(p1Left);
            _p1RightRenderer = EnsureRenderer(p1Right);
            _p2LeftRenderer = EnsureRenderer(p2Left);
            _p2RightRenderer = EnsureRenderer(p2Right);
        }

        private SpriteRenderer EnsureRenderer(Transform target)
        {
            if (target == null) return null;

            var renderer = target.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                renderer = target.gameObject.AddComponent<SpriteRenderer>();
            }

            renderer.sortingOrder = spriteSortingOrder;
            return renderer;
        }

        private void AutoFillSpritesFromProjectIfNeeded()
        {
            if (blueHandSprites != null && blueHandSprites.Length >= 7 && blueHandSprites.All(s => s != null) &&
                redHandSprites != null && redHandSprites.Length >= 7 && redHandSprites.All(s => s != null))
            {
                return;
            }

            if (blueHandSprites == null || blueHandSprites.Length != 7) blueHandSprites = new Sprite[7];
            if (redHandSprites == null || redHandSprites.Length != 7) redHandSprites = new Sprite[7];

            var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            for (var i = 1; i <= 7; i++)
            {
                if (blueHandSprites[i - 1] == null)
                {
                    blueHandSprites[i - 1] = sprites.FirstOrDefault(s => s.name == $"blue{i}");
                }

                if (redHandSprites[i - 1] == null)
                {
                    redHandSprites[i - 1] = sprites.FirstOrDefault(s => s.name == $"red{i}");
                }
            }
        }

        private void RefreshHandSprites()
        {
            var state = GameConfig.CurrentState;
            ApplyValueSprite(_p1LeftRenderer, state.WhiteA, blueHandSprites);
            ApplyValueSprite(_p1RightRenderer, state.WhiteB, blueHandSprites);
            ApplyValueSprite(_p2LeftRenderer, state.BlackA, redHandSprites);
            ApplyValueSprite(_p2RightRenderer, state.BlackB, redHandSprites);
        }

        private static void ApplyValueSprite(SpriteRenderer renderer, int value, Sprite[] sprites)
        {
            if (renderer == null) return;

            if (value <= 0)
            {
                renderer.sprite = null;
                renderer.enabled = false;
                return;
            }

            if (sprites == null || sprites.Length < value || sprites[value - 1] == null)
            {
                renderer.sprite = null;
                renderer.enabled = false;
                return;
            }

            renderer.sprite = sprites[value - 1];
            renderer.enabled = true;
        }

        private static void DrawMissingBrainMessage()
        {
            GUILayout.BeginArea(new Rect(20f, 20f, 420f, 120f), GUI.skin.box);
            GUILayout.Label("EightGamePlayableController: EightBotBrain reference is missing.");
            GUILayout.Label("Attach this script to the same GameObject as EightBotBrain.");
            GUILayout.EndArea();
        }
    }
}
