using System;
using System.Collections.Generic;
using System.Linq;
using SimOps.Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimOps.Unity
{
    // All layout/feedback lives outside the deterministic simulation.
    public sealed class ArenaView
    {
        public VisualElement Root { get; }
        private readonly Label _mode, _progress, _turn, _playerHp, _enemyHp, _playerStats, _enemyStats, _intent, _enemyName;
        private readonly Label _message, _resultTitle, _resultScore, _resultStats, _submission, _online, _debugText, _feedback;
        private readonly VisualElement _playerFill, _enemyFill, _playerPanel, _enemyPanel, _battle, _rewards, _result, _actionRow, _rewardCards;
        private readonly List<Label> _steps = new List<Label>();
        private readonly Dictionary<GameActionType, Button> _actions = new Dictionary<GameActionType, Button>();
        private readonly Button _submit, _practice, _ranked, _ranking;
        private readonly VisualElement _confirm;
        private readonly Label _confirmText;
        private readonly Button _confirmYes;
        private readonly VisualElement _page;
        private Action _pending;
        private string _rewardSignature = "";
        private string _sessionMode = "";
        public bool ConfirmOpen => _pending != null;
        public string SubmissionText => _submission.text;
        public bool IsActionEnabled(GameActionType type) => _actions[type].enabledInHierarchy;
        public bool IsSubmitEnabled => _submit.enabledInHierarchy;
        public bool HasPhase(RunPhase phase) => (phase == RunPhase.RewardChoice ? _rewards : phase == RunPhase.Terminal ? _result : _battle).style.display != DisplayStyle.None;
        public bool CriticalControlsVisible(RunPhase phase)
        {
            var viewport = Root.Q<ScrollView>().contentViewport.worldBound;
            var controls = phase == RunPhase.PlayerTurn ? _actions.Values.Cast<VisualElement>()
                : phase == RunPhase.RewardChoice ? _rewardCards.Children() : new[] { _result };
            return controls.All(control => control.worldBound.width > 0 && control.worldBound.height > 0 &&
                control.worldBound.xMin >= viewport.xMin && control.worldBound.xMax <= viewport.xMax + 1 &&
                control.worldBound.yMin >= viewport.yMin && control.worldBound.yMax <= viewport.yMax + 1);
        }

        public ArenaView(VisualElement root, Action<GameActionType, string> perform, Action practice, Action ranked, Action submit, Action ranking, Action replay)
        {
            Root = root;
            root.AddToClassList("arena");
            root.styleSheets.Add(Resources.Load<StyleSheet>("Arena"));
            var font = Resources.Load<Font>("Fonts/NotoSansKR-Regular");
            if (font == null) throw new InvalidOperationException("Korean font asset is missing.");
            root.style.unityFontDefinition = FontDefinition.FromFont(font);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "arena-scroll" };
            scroll.AddToClassList("page-scroll"); root.Add(scroll);
            var page = Box(scroll, "page"); _page = page;
            var header = Box(page, "header");
            var brand = Box(header, "brand"); Label(brand, "SIMOPS / ARENA", "eyebrow"); Label(brand, "여섯 번의 전투, 하나의 기록.", "brand-title");
            _mode = Label(header, "연습 모드", "mode");
            var toolbar = Box(page, "toolbar");
            _progress = Label(toolbar, "", "section-title"); _turn = Label(toolbar, "", "muted");
            var journey = Box(page, "journey");
            for (var i = 1; i <= 6; i++) _steps.Add(Label(journey, i == 6 ? "06  보스" : $"0{i}  전투", "step"));

            var combatants = Box(page, "combatants");
            _playerPanel = Box(combatants, "fighter player"); Label(_playerPanel, "PLAYER / 도전자", "eyebrow");
            var playerTop = Box(_playerPanel, "fighter-title"); Label(playerTop, "당신의 상태", "fighter-name"); _playerHp = Label(playerTop, "", "health-number");
            _playerFill = Box(Box(_playerPanel, "health-track"), "health-fill");
            _playerStats = Label(_playerPanel, "", "stats");
            _enemyPanel = Box(combatants, "fighter enemy"); Label(_enemyPanel, "ENEMY / 상대", "eyebrow");
            var enemyTop = Box(_enemyPanel, "fighter-title"); _enemyName = Label(enemyTop, "", "fighter-name"); _enemyHp = Label(enemyTop, "", "health-number");
            _enemyFill = Box(Box(_enemyPanel, "health-track"), "health-fill enemy-fill");
            _enemyStats = Label(_enemyPanel, "", "stats");
            _feedback = Label(page, "", "feedback");

            _battle = Box(page, "phase");
            var intentPanel = Box(_battle, "intent-panel"); Label(intentPanel, "상대의 다음 행동", "eyebrow"); _intent = Label(intentPanel, "", "intent");
            Label(intentPanel, "행동력을 모두 쓰거나 턴을 종료하면 상대가 행동합니다. 피해 수치는 방어도 적용 전입니다.", "muted");
            _actionRow = Box(_battle, "action-row");
            for (var i = 0; i < 5; i++)
            {
                var type = (GameActionType)i;
                var button = Button(_actionRow, "", () => perform(type, null), "action-card");
                button.name = "action-" + type;
                if (type == GameActionType.EndTurn) button.AddToClassList("end-turn");
                _actions[type] = button;
            }
            Label(_battle, "키보드 1–5 / Space: 턴 종료   ·   모바일: 버튼 터치", "muted controls-hint");

            _rewards = Box(page, "phase reward-phase"); Label(_rewards, "전투 승리", "eyebrow");
            Label(_rewards, "다음 전투를 위한 강화", "phase-title");
            Label(_rewards, "하나를 선택하면 효과가 즉시 적용되고 다음 전투가 시작됩니다.", "muted");
            _rewardCards = Box(_rewards, "reward-row");

            _result = Box(page, "phase result-phase"); Label(_result, "RUN COMPLETE", "eyebrow");
            _resultTitle = Label(_result, "", "phase-title"); _resultScore = Label(_result, "", "score");
            _resultStats = Label(_result, "", "stats");
            _submission = Label(_result, "", "submission");
            var resultActions = Box(_result, "result-actions");
            _submit = Button(resultActions, "결과 제출", submit, "primary"); _submit.name = "submit-result";
            _ranking = Button(resultActions, "시즌 랭킹 확인", ranking, "secondary");

            _message = Label(page, "", "message");
            var footer = Box(page, "footer");
            _practice = Button(footer, "새 연습 시작", practice, "secondary");
            _ranked = Button(footer, "랭킹 도전 시작", ranked, "primary");
            _online = Label(page, "", "online-status");
            var debug = new Foldout { text = "개발 정보 · 리플레이", value = false, name = "debug-foldout" };
            debug.AddToClassList("debug"); page.Add(debug);
            _debugText = Label(debug, "", "debug-text"); Button(debug, "현재 행동 로그 재검증", replay, "secondary");

            _confirm = Box(root, "confirm-backdrop");
            var dialog = Box(_confirm, "confirm-dialog"); Label(dialog, "새 도전을 시작할까요?", "phase-title");
            _confirmText = Label(dialog, "", "message");
            var confirmActions = Box(dialog, "result-actions");
            Button(confirmActions, "계속 플레이", CancelConfirm, "secondary");
            _confirmYes = Button(confirmActions, "새 도전 시작", () => { var action = _pending; CancelConfirm(); action?.Invoke(); }, "primary");
            _confirm.style.display = DisplayStyle.None;
        }

        public void Confirm(string text, Action accepted)
        {
            _pending = accepted; _page.SetEnabled(false); _confirmText.text = text; _confirm.style.display = DisplayStyle.Flex;
            _confirm.Q<Button>().Focus();
        }
        public event Action DialogClosed;
        public void CancelConfirm() { _pending = null; _page.SetEnabled(true); _confirm.style.display = DisplayStyle.None; DialogClosed?.Invoke(); }

        public void Render(GameConfig config, GameObservation observation, IReadOnlyList<GameAction> actions, RunResult result,
            bool ranked, string message, string onlineStatus, string submissionStatus, bool verified, bool busy, string debug)
        {
            var player = observation.Player; var enemy = observation.Enemy;
            _sessionMode = ranked ? "랭킹 도전" : "오프라인 연습";
            _mode.text = _sessionMode; _mode.EnableInClassList("ranked", ranked);
            _progress.text = $"STAGE {observation.Stage:00} / 06";
            _turn.text = $"{observation.Turn}턴 · 누적 {observation.TotalTurns}턴";
            for (var i = 0; i < _steps.Count; i++)
            {
                var done = i + 1 < observation.Stage || (i + 1 == observation.Stage && (observation.Phase == RunPhase.RewardChoice || observation.Outcome == RunOutcome.Victory));
                _steps[i].EnableInClassList("done", done); _steps[i].EnableInClassList("current", i + 1 == observation.Stage && !done);
            }
            _playerHp.text = $"{player.CurrentHealth} / {player.MaxHealth}";
            _playerFill.style.width = Length.Percent(Mathf.Clamp01((float)player.CurrentHealth / player.MaxHealth) * 100);
            _playerPanel.EnableInClassList("low-health", player.CurrentHealth * 3 <= player.MaxHealth);
            _playerStats.text = $"행동력  {player.ActionPoints}     방어도  {player.Block}     공격력  {player.Attack}     회복약  {player.ItemCharges}";
            _enemyName.text = enemy == null ? "전투 종료" : ArenaText.EnemyName(enemy.EncounterId);
            _enemyHp.text = enemy == null ? "—" : $"{enemy.CurrentHealth} / {enemy.MaxHealth}";
            _enemyFill.style.width = Length.Percent(enemy == null ? 0 : Mathf.Clamp01((float)enemy.CurrentHealth / enemy.MaxHealth) * 100);
            _enemyStats.text = enemy == null ? "" : $"방어도  {enemy.Block}     기본 공격력  {enemy.AttackPower + enemy.AttackBonus}";
            _intent.text = ArenaText.Intent(config, observation);
            _intent.EnableInClassList("danger-text", enemy?.Intent == EnemyIntentType.HeavyAttack);
            _battle.style.display = observation.Phase == RunPhase.PlayerTurn ? DisplayStyle.Flex : DisplayStyle.None;
            _rewards.style.display = observation.Phase == RunPhase.RewardChoice ? DisplayStyle.Flex : DisplayStyle.None;
            _result.style.display = observation.Phase == RunPhase.Terminal ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var pair in _actions)
            {
                var reason = ArenaText.DisabledReason(pair.Key, observation);
                var extra = pair.Key == GameActionType.Technique ? $"재사용 {Math.Max(0, config.TechniqueCooldownTurns - ArenaText.Bonus(config, actions, RewardEffectType.TechniqueCooldownReduction))}턴" : pair.Key == GameActionType.UseItem ? "턴당 1회" : "";
                pair.Value.text = $"{(int)pair.Key + 1}  {ArenaText.ActionName(pair.Key)}\n{ArenaText.ActionEffect(pair.Key, config, observation, actions)}\n{(reason.Length > 0 ? reason : extra)}";
                pair.Value.SetEnabled(reason.Length == 0 && !busy && !ConfirmOpen);
            }
            var signature = observation.Phase == RunPhase.RewardChoice ? string.Join("/", observation.OfferedRewardIds) : "";
            if (signature != _rewardSignature)
            {
                _rewardCards.Clear(); _rewardSignature = signature;
                foreach (var id in observation.Phase == RunPhase.RewardChoice ? observation.OfferedRewardIds : Array.Empty<string>())
                {
                    var reward = config.Rewards.Single(r => r.Id == id);
                    var card = Button(_rewardCards, $"{ArenaText.Category(reward.Category)}\n\n{ArenaText.RewardName(reward)}\n{ArenaText.RewardDescription(reward)}\n\n선택하고 다음 전투", () => RewardSelected?.Invoke(id), "reward-card");
                    card.AddToClassList("reward-" + reward.Category.ToString().ToLowerInvariant());
                }
            }
            _rewardCards.SetEnabled(!busy && !ConfirmOpen);
            if (result != null)
            {
                _resultTitle.text = ArenaText.Outcome(result.Outcome);
                _resultScore.text = result.FinalScore.ToString("N0") + " 점";
                _resultStats.text = $"통과 {result.ClearedStages}/6   ·   총 {result.TotalTurns}턴   ·   남은 체력 {result.FinalHealth}/{result.MaxHealth}";
            }
            _submission.text = ranked ? submissionStatus : "연습 기록입니다. 서버 검증과 시즌 랭킹에는 등록되지 않습니다.";
            _submit.style.display = ranked ? DisplayStyle.Flex : DisplayStyle.None;
            _submit.text = verified ? "서버 검증 완료" : busy ? "처리 중…" : "결과 제출 / 상태 재확인";
            _submit.SetEnabled(ranked && !busy && !verified);
            _ranking.SetEnabled(!busy); _practice.SetEnabled(!busy); _ranked.SetEnabled(!busy);
            _message.text = message; _online.text = onlineStatus; _debugText.text = debug;
        }
        public event Action<string> RewardSelected;
        private int _feedbackVersion;
        public void Feedback(GameObservation before, GameObservation after)
        {
            var parts = new List<string>();
            var hp = after.Player.CurrentHealth - before.Player.CurrentHealth;
            if (hp != 0) parts.Add(hp > 0 ? $"체력 +{hp}" : $"받은 피해 {-hp}");
            if (before.Stage == after.Stage && before.Enemy != null && after.Enemy != null)
            {
                var damage = before.Enemy.CurrentHealth - after.Enemy.CurrentHealth;
                if (damage > 0) parts.Add($"적에게 {damage} 피해");
            }
            if (after.Player.Block > before.Player.Block) parts.Add($"방어도 +{after.Player.Block - before.Player.Block}");
            _feedback.text = string.Join("  ·  ", parts);
            _playerPanel.EnableInClassList("hit", hp < 0); _playerPanel.EnableInClassList("heal", hp > 0);
            _enemyPanel.EnableInClassList("hit", parts.Any(x => x.StartsWith("적에게", StringComparison.Ordinal)));
            var version = ++_feedbackVersion;
            Root.schedule.Execute(() => { if (version != _feedbackVersion) return; _playerPanel.RemoveFromClassList("hit"); _playerPanel.RemoveFromClassList("heal"); _enemyPanel.RemoveFromClassList("hit"); _feedback.text = ""; }).StartingIn(700);
        }
        public void ClearFeedback() { _feedbackVersion++; _feedback.text = ""; _playerPanel.RemoveFromClassList("hit"); _playerPanel.RemoveFromClassList("heal"); _enemyPanel.RemoveFromClassList("hit"); }
        public void Resize()
        {
            Root.EnableInClassList("compact", Root.resolvedStyle.width < 1100);
            Root.EnableInClassList("narrow", Root.resolvedStyle.width < 760);
        }
        private static VisualElement Box(VisualElement parent, string classes)
        { var box = new VisualElement(); foreach (var cls in classes.Split(' ')) box.AddToClassList(cls); parent.Add(box); return box; }
        private static Label Label(VisualElement parent, string text, string classes)
        { var label = new Label(text); foreach (var cls in classes.Split(' ')) label.AddToClassList(cls); parent.Add(label); return label; }
        private static Button Button(VisualElement parent, string text, Action clicked, string classes)
        { var button = new Button(clicked) { text = text }; foreach (var cls in classes.Split(' ')) button.AddToClassList(cls); parent.Add(button); return button; }
    }
}
