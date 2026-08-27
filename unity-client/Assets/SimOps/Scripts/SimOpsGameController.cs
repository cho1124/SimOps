using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SimOps.Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimOps.Unity
{
    public sealed class SimOpsGameController : MonoBehaviour
    {
        private const ulong DefaultSeed = 42UL;
        private static readonly Color BackgroundColor = new Color(0.035f, 0.055f, 0.09f, 1f);
        private static readonly Color PanelColor = new Color(0.075f, 0.105f, 0.16f, 0.98f);
        private static readonly Color ButtonColor = new Color(0.12f, 0.24f, 0.36f, 1f);
        private static readonly Color AccentColor = new Color(0.22f, 0.78f, 0.68f, 1f);

        private readonly Dictionary<GameActionType, Button> _actionButtons =
            new Dictionary<GameActionType, Button>();

        private GameConfig _config;
        private ScoreRule _scoreRule;
        private RunContext _context;
        private GameSimulation _simulation;
        private GameObservation _observation;
        private ReplayStore _replayStore;
        private Label _statusLabel;
        private Label _messageLabel;
        private Label _historyLabel;
        private VisualElement _rewardContainer;
        private VisualElement _safeAreaRoot;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;
        private bool _smokeMode;
        private string _message = string.Empty;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            _smokeMode = HasCommandLineArgument("--simops-smoke");
            _config = GameConfig.CreateBaseline();
            _scoreRule = ScoreRule.CreateBaseline();
            _replayStore = new ReplayStore();
            BuildInterface();

            if (!TryResume())
            {
                StartNewRun(DefaultSeed);
            }

            if (_smokeMode)
            {
                StartCoroutine(RunSmokeTest());
            }
        }

        private void Update()
        {
            ApplySafeAreaIfNeeded();
            if (_observation == null || _observation.Phase != RunPhase.PlayerTurn)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                Perform(GameActionType.Strike);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                Perform(GameActionType.Guard);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                Perform(GameActionType.Technique);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                Perform(GameActionType.UseItem);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Space))
            {
                Perform(GameActionType.EndTurn);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                SaveProgress();
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                SaveProgress();
            }
        }

        private void OnApplicationQuit()
        {
            SaveProgress();
        }

        private void StartNewRun(ulong seed)
        {
            _context = CreateContext(seed);
            _simulation = new GameSimulation(_config, _scoreRule);
            _observation = _simulation.Reset(_context);
            _message = $"New deterministic run · seed {seed.ToString(CultureInfo.InvariantCulture)}";
            SaveProgress();
            RefreshInterface();
        }

        private bool TryResume()
        {
            if (!_replayStore.TryLoad(out var replay) ||
                !ulong.TryParse(replay.baseSeed, NumberStyles.None, CultureInfo.InvariantCulture, out var seed) ||
                !string.Equals(replay.gameVersion, _config.GameVersion, StringComparison.Ordinal) ||
                !string.Equals(replay.configChecksum, _config.Checksum, StringComparison.Ordinal) ||
                !string.Equals(replay.scoreRuleVersion, _scoreRule.Version, StringComparison.Ordinal) ||
                !string.Equals(replay.scoreRuleChecksum, _scoreRule.Checksum, StringComparison.Ordinal))
            {
                return false;
            }

            _context = CreateContext(seed);
            _simulation = new GameSimulation(_config, _scoreRule);
            _observation = _simulation.Reset(_context);

            for (var index = 0; index < replay.actions.Count; index++)
            {
                var record = replay.actions[index];
                var rewardId = string.IsNullOrEmpty(record.rewardId) ? null : record.rewardId;
                var step = _simulation.Apply(
                    new GameAction(record.sequence, (GameActionType)record.actionType, rewardId));
                if (!step.Accepted)
                {
                    Debug.LogWarning($"Replay rejected action {record.sequence}: {step.RejectionCode}");
                    return false;
                }

                _observation = step.Observation;
            }

            _message = $"Resumed {_simulation.ActionLog.Count.ToString(CultureInfo.InvariantCulture)} saved actions";
            RefreshInterface();
            return true;
        }

        private void ReplayCurrentLog()
        {
            if (_simulation == null)
            {
                return;
            }

            var actions = _simulation.ActionLog;
            var replay = new GameSimulation(_config, _scoreRule);
            var observation = replay.Reset(_context);

            for (var index = 0; index < actions.Count; index++)
            {
                var step = replay.Apply(actions[index]);
                if (!step.Accepted)
                {
                    _message = $"Replay rejected action {index}: {step.RejectionCode}";
                    RefreshInterface();
                    return;
                }

                observation = step.Observation;
            }

            _simulation = replay;
            _observation = observation;
            _message = observation.Phase == RunPhase.Terminal
                ? $"Replay verified · {_simulation.GetCanonicalResult().ResultHash.Substring(0, 12)}…"
                : $"Replay restored {actions.Count.ToString(CultureInfo.InvariantCulture)} actions";
            SaveProgress();
            RefreshInterface();
        }

        private void Perform(GameActionType actionType, string rewardId = null)
        {
            if (_observation == null || !IsValid(actionType))
            {
                return;
            }

            var step = _simulation.Apply(
                new GameAction(_observation.NextActionSequence, actionType, rewardId));
            if (!step.Accepted)
            {
                _message = $"Rejected: {step.RejectionCode}";
                RefreshInterface();
                return;
            }

            _observation = step.Observation;
            _message = step.DomainEvents.Count > 0
                ? string.Join(" · ", step.DomainEvents)
                : actionType.ToString();
            SaveProgress();
            RefreshInterface();
        }

        private void SaveProgress()
        {
            if (_smokeMode || _simulation == null || _context == null)
            {
                return;
            }

            try
            {
                _replayStore.Save(_context, _simulation.ActionLog);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Replay save failed: {exception.Message}");
            }
        }

        private RunContext CreateContext(ulong seed)
        {
            return new RunContext(
                _config.GameVersion,
                _config.Checksum,
                _scoreRule.Version,
                _scoreRule.Checksum,
                seed);
        }

        private bool IsValid(GameActionType actionType)
        {
            if (_observation == null)
            {
                return false;
            }

            for (var index = 0; index < _observation.ValidActionTypes.Count; index++)
            {
                if (_observation.ValidActionTypes[index] == actionType)
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshInterface()
        {
            if (_observation == null)
            {
                return;
            }

            var player = _observation.Player;
            var enemy = _observation.Enemy;
            var status = new StringBuilder();
            status.AppendLine($"STAGE {_observation.Stage}/6     TURN {_observation.Turn}     SEED {_context.BaseSeed}");
            status.AppendLine($"PLAYER  HP {player.CurrentHealth}/{player.MaxHealth}   AP {player.ActionPoints}   BLOCK {player.Block}");
            if (enemy != null && _observation.Phase != RunPhase.Terminal)
            {
                status.AppendLine($"ENEMY   {enemy.EncounterId}   HP {enemy.CurrentHealth}/{enemy.MaxHealth}   BLOCK {enemy.Block}");
                status.Append($"INTENT  {enemy.Intent}   ATK {enemy.AttackPower}+{enemy.AttackBonus}");
            }

            if (_observation.Phase == RunPhase.Terminal)
            {
                var result = _simulation.GetCanonicalResult();
                status.AppendLine($"RESULT  {result.Outcome}   SCORE {result.FinalScore}");
                status.Append($"HASH    {result.ResultHash}");
            }

            _statusLabel.text = status.ToString();
            _messageLabel.text = _message;

            foreach (var entry in _actionButtons)
            {
                entry.Value.SetEnabled(IsValid(entry.Key));
            }

            RebuildRewards();
            RebuildHistory();
        }

        private void RebuildRewards()
        {
            _rewardContainer.Clear();
            if (_observation.Phase != RunPhase.RewardChoice)
            {
                return;
            }

            for (var index = 0; index < _observation.OfferedRewardIds.Count; index++)
            {
                var rewardId = _observation.OfferedRewardIds[index];
                var capturedRewardId = rewardId;
                _rewardContainer.Add(CreateButton(
                    FormatReward(rewardId),
                    () => Perform(GameActionType.ChooseReward, capturedRewardId),
                    250f));
            }
        }

        private void RebuildHistory()
        {
            var actions = _simulation.ActionLog;
            var first = Math.Max(0, actions.Count - 6);
            var history = new StringBuilder("ACTION LOG");
            for (var index = first; index < actions.Count; index++)
            {
                var action = actions[index];
                history.AppendLine();
                history.Append('#').Append(action.Sequence).Append(' ').Append(action.ActionType);
                if (!string.IsNullOrEmpty(action.RewardId))
                {
                    history.Append(" · ").Append(FormatReward(action.RewardId));
                }
            }

            _historyLabel.text = history.ToString();
        }

        private void BuildInterface()
        {
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "SimOps Runtime Panel";
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1600, 900);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;

            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            var root = document.rootVisualElement;
            root.name = "simops-root";
            root.style.flexGrow = 1f;
            root.style.backgroundColor = BackgroundColor;

            _safeAreaRoot = new VisualElement { name = "safe-area" };
            _safeAreaRoot.style.flexGrow = 1f;
            _safeAreaRoot.style.backgroundColor = PanelColor;
            _safeAreaRoot.style.paddingLeft = 36f;
            _safeAreaRoot.style.paddingRight = 36f;
            _safeAreaRoot.style.paddingTop = 28f;
            _safeAreaRoot.style.paddingBottom = 28f;
            root.Add(_safeAreaRoot);

            var title = CreateLabel("SIMOPS // DETERMINISTIC ARENA", 32, FontStyle.Bold);
            title.style.color = AccentColor;
            title.style.height = 48f;
            _safeAreaRoot.Add(title);

            _statusLabel = CreateLabel(string.Empty, 24, FontStyle.Normal);
            _statusLabel.style.height = 150f;
            _safeAreaRoot.Add(_statusLabel);

            var actions = CreateRow(74f);
            _actionButtons[GameActionType.Strike] = AddActionButton(actions, GameActionType.Strike, "1  STRIKE");
            _actionButtons[GameActionType.Guard] = AddActionButton(actions, GameActionType.Guard, "2  GUARD");
            _actionButtons[GameActionType.Technique] = AddActionButton(actions, GameActionType.Technique, "3  TECHNIQUE");
            _actionButtons[GameActionType.UseItem] = AddActionButton(actions, GameActionType.UseItem, "4  ITEM");
            _actionButtons[GameActionType.EndTurn] = AddActionButton(actions, GameActionType.EndTurn, "5  END TURN");
            _safeAreaRoot.Add(actions);

            _rewardContainer = CreateRow(74f);
            _safeAreaRoot.Add(_rewardContainer);

            _messageLabel = CreateLabel(string.Empty, 20, FontStyle.Italic);
            _messageLabel.style.color = AccentColor;
            _messageLabel.style.height = 36f;
            _safeAreaRoot.Add(_messageLabel);

            _historyLabel = CreateLabel(string.Empty, 18, FontStyle.Normal);
            _historyLabel.style.flexGrow = 1f;
            _historyLabel.style.minHeight = 120f;
            _safeAreaRoot.Add(_historyLabel);

            var footer = CreateRow(64f);
            footer.Add(CreateButton("REPLAY CURRENT LOG", ReplayCurrentLog, 300f));
            footer.Add(CreateButton("NEW SEED 42 RUN", () => StartNewRun(DefaultSeed), 300f));
            _safeAreaRoot.Add(footer);
            ApplySafeAreaIfNeeded(true);
        }

        private Button AddActionButton(VisualElement parent, GameActionType actionType, string label)
        {
            var button = CreateButton(label, () => Perform(actionType), 150f);
            parent.Add(button);
            return button;
        }

        private static VisualElement CreateRow(float height)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = height;
            row.style.marginBottom = 12f;
            return row;
        }

        private static Label CreateLabel(string value, int size, FontStyle style)
        {
            var label = new Label(value);
            label.style.fontSize = size;
            label.style.unityFontStyleAndWeight = style;
            label.style.color = Color.white;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static Button CreateButton(string label, Action action, float minimumWidth)
        {
            var button = new Button(action) { text = label };
            button.style.flexGrow = 1f;
            button.style.minWidth = minimumWidth;
            button.style.marginLeft = 6f;
            button.style.marginRight = 6f;
            button.style.marginTop = 4f;
            button.style.marginBottom = 4f;
            button.style.backgroundColor = ButtonColor;
            button.style.color = Color.white;
            button.style.fontSize = 18f;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.borderTopLeftRadius = 6f;
            button.style.borderTopRightRadius = 6f;
            button.style.borderBottomLeftRadius = 6f;
            button.style.borderBottomRightRadius = 6f;
            return button;
        }

        private void ApplySafeAreaIfNeeded(bool force = false)
        {
            if (_safeAreaRoot == null)
            {
                return;
            }

            var screenSize = new Vector2Int(Screen.width, Screen.height);
            var safeArea = Screen.safeArea;
            if (!force && _lastSafeArea == safeArea && _lastScreenSize == screenSize)
            {
                return;
            }

            var scale = Mathf.Max(
                0.01f,
                Mathf.Lerp(Screen.width / 1600f, Screen.height / 900f, 0.5f));
            _safeAreaRoot.style.paddingLeft = 36f + (safeArea.xMin / scale);
            _safeAreaRoot.style.paddingRight = 36f + ((Screen.width - safeArea.xMax) / scale);
            _safeAreaRoot.style.paddingTop = 28f + ((Screen.height - safeArea.yMax) / scale);
            _safeAreaRoot.style.paddingBottom = 28f + (safeArea.yMin / scale);
            _lastSafeArea = safeArea;
            _lastScreenSize = screenSize;
        }

        private static string FormatReward(string rewardId)
        {
            return rewardId.Replace("offense-", string.Empty)
                .Replace("defense-", string.Empty)
                .Replace("sustain-", string.Empty)
                .Replace("tactics-", string.Empty)
                .Replace('-', ' ')
                .ToUpperInvariant();
        }

        private IEnumerator RunSmokeTest()
        {
            yield return null;
            yield return null;

            if (_statusLabel == null || _observation == null || _observation.Stage != 1)
            {
                Debug.LogError("SIMOPS_PLAYER_SMOKE_FAIL interface or initial state missing");
                Application.Quit(1);
                yield break;
            }

            Perform(GameActionType.Strike);
            if (_simulation.ActionLog.Count != 1)
            {
                Debug.LogError("SIMOPS_PLAYER_SMOKE_FAIL action was not applied");
                Application.Quit(1);
                yield break;
            }

            Debug.Log("SIMOPS_PLAYER_SMOKE_PASS interface=ready actionCount=1");
            Application.Quit(0);
        }

        private static bool HasCommandLineArgument(string expected)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
