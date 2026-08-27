using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SimOps.Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimOps.Unity
{
    public sealed class SimOpsGameController : MonoBehaviour
    {
        private const ulong DefaultSeed = 42UL;
        private GameConfig _config;
        private ScoreRule _scoreRule;
        private RunContext _context;
        private GameSimulation _simulation;
        private GameObservation _observation;
        private ReplayStore _replayStore;
        private ArenaView _view;
        private VisualElement _safeAreaRoot;
        private RenderTexture _previewTexture;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;
        private bool _smokeMode, _onlineSmokeMode, _uiPreview, _lastBusy, _verified;
        private SimOpsOnlineClient _online;
        private OnlineTicketData _activeTicket;
        private string _message = "", _onlineStatus = "연습은 로컬에만 저장됩니다. 랭킹 도전은 로컬 서버 연결이 필요합니다.";
        private string _submissionStatus = "결과 미제출 · 아래 버튼으로 서버 검증을 요청하세요.";

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            if (Camera.main == null)
            {
                var cameraObject = new GameObject("SimOps UI Camera"); cameraObject.tag = "MainCamera";
                var camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.035f, 0.07f, 0.09f); camera.cullingMask = 0;
            }
            _onlineSmokeMode = HasArgument("--simops-online-smoke"); _uiPreview = HasArgument("--simops-ui-preview");
            _smokeMode = _onlineSmokeMode || _uiPreview || HasArgument("--simops-smoke");
            _online = gameObject.AddComponent<SimOpsOnlineClient>();
            _config = GameConfig.CreateBaseline(); _scoreRule = ScoreRule.CreateBaseline(); _replayStore = new ReplayStore();
            BuildInterface();
            _online.StatusChanged += OnOnlineStatus;
            if (_smokeMode || !TryResume()) StartNewRun(DefaultSeed);
            if (_uiPreview) StartCoroutine(RunUiPreview());
            else if (_onlineSmokeMode) StartCoroutine(RunOnlineSmokeTest());
            else if (_smokeMode) StartCoroutine(RunSmokeTest());
        }
        private void OnDestroy()
        {
            if (_online != null) _online.StatusChanged -= OnOnlineStatus;
            if (_previewTexture != null) { _previewTexture.Release(); Destroy(_previewTexture); }
        }
        private void OnOnlineStatus(string text) { _onlineStatus = text; RefreshInterface(); }
        private void Update()
        {
            ApplySafeAreaIfNeeded();
            if (_lastBusy != _online.Busy) { _lastBusy = _online.Busy; RefreshInterface(); }
            if (Input.GetKeyDown(KeyCode.Escape) && _view.ConfirmOpen) { _view.CancelConfirm(); RefreshInterface(); }
            if (_observation == null || _observation.Phase != RunPhase.PlayerTurn || _online.Busy || _view.ConfirmOpen) return;
            if (Input.GetKeyDown(KeyCode.Alpha1)) Perform(GameActionType.Strike);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) Perform(GameActionType.Guard);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) Perform(GameActionType.Technique);
            else if (Input.GetKeyDown(KeyCode.Alpha4)) Perform(GameActionType.UseItem);
            else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Space)) Perform(GameActionType.EndTurn);
        }
        private void OnApplicationPause(bool paused) { if (paused) SaveProgress(); }
        private void OnApplicationFocus(bool focused) { if (!focused) SaveProgress(); }
        private void OnApplicationQuit() => SaveProgress();

        private void RequestNewRun(bool ranked)
        {
            if (_online.Busy) return;
            Action begin = () => { if (ranked) StartCoroutine(_online.Begin(AcceptTicket)); else StartNewRun(DefaultSeed); };
            if (_simulation.ActionLog.Count > 0 || _activeTicket != null)
                _view.Confirm(_activeTicket != null && !_verified
                    ? "현재 랭킹 기록은 검증 완료를 확인하지 못했습니다. 새 도전을 시작하면 이 PC의 기존 기록을 덮어씁니다."
                    : "로컬에는 최신 한 판만 보관됩니다. 현재 진행과 행동 로그를 새 기록으로 바꿉니다.", begin);
            else begin();
            RefreshInterface();
        }
        private void StartNewRun(ulong seed, GameConfig config = null)
        {
            _config = config ?? GameConfig.CreateBaseline(); _activeTicket = null; _verified = false;
            _submissionStatus = "결과 미제출 · 아래 버튼으로 서버 검증을 요청하세요.";
            _context = CreateContext(seed); _simulation = new GameSimulation(_config, _scoreRule); _observation = _simulation.Reset(_context);
            _view.ClearFeedback(); _message = "적의 다음 행동을 확인하고 행동력을 사용하세요.";
            _onlineStatus = "연습은 로컬에만 저장됩니다. 랭킹 기록은 결과 화면에서 제출하세요.";
            SaveProgress(); RefreshInterface();
        }
        private RunContext CreateContext(ulong seed) => new RunContext(_config.GameVersion, _config.Checksum, _scoreRule.Version, _scoreRule.Checksum, seed);
        private bool TryResume()
        {
            if (!_replayStore.TryLoad(out var replay)) return false;
            if (replay.onlineTicket?.config != null)
            { try { _config = replay.onlineTicket.config.ToConfig(); } catch (ArgumentException) { return false; } }
            if (!ulong.TryParse(replay.baseSeed, NumberStyles.None, CultureInfo.InvariantCulture, out var seed) ||
                replay.gameVersion != _config.GameVersion || replay.configChecksum != _config.Checksum ||
                replay.scoreRuleVersion != _scoreRule.Version || replay.scoreRuleChecksum != _scoreRule.Checksum) return false;
            _context = CreateContext(seed); _simulation = new GameSimulation(_config, _scoreRule); _observation = _simulation.Reset(_context);
            foreach (var record in replay.actions)
            {
                var step = _simulation.Apply(new GameAction(record.sequence, (GameActionType)record.actionType, string.IsNullOrEmpty(record.rewardId) ? null : record.rewardId));
                if (!step.Accepted) { Debug.LogWarning("Replay rejected: " + step.RejectionCode); return false; }
                _observation = step.Observation;
            }
            _activeTicket = replay.onlineTicket;
            _message = $"저장된 행동 {_simulation.ActionLog.Count}개를 복구했습니다.";
            _submissionStatus = "저장 기록 복구 · 서버 검증 상태는 아래 버튼으로 재확인하세요.";
            if (_activeTicket != null) _onlineStatus = "랭킹 도전 기록을 복구했습니다. 결과는 서버 검증 후 반영됩니다.";
            RefreshInterface(); return true;
        }
        private void ReplayCurrentLog()
        {
            if (_simulation == null || _online.Busy || _view.ConfirmOpen) return;
            var replay = new GameSimulation(_config, _scoreRule); var observation = replay.Reset(_context);
            foreach (var action in _simulation.ActionLog)
            {
                var step = replay.Apply(action);
                if (!step.Accepted) { _message = "리플레이 검증 실패: " + step.RejectionCode; RefreshInterface(); return; }
                observation = step.Observation;
            }
            _simulation = replay; _observation = observation; _view.ClearFeedback();
            _message = "동일한 행동 로그로 게임 상태를 재현했습니다. 서버 검증과는 별개입니다.";
            SaveProgress(); RefreshInterface();
        }
        private bool IsValid(GameActionType actionType) => _observation != null && _observation.ValidActionTypes.Contains(actionType);
        private void Perform(GameActionType actionType, string rewardId = null)
        {
            if (!IsValid(actionType) || _online.Busy || _view.ConfirmOpen) return;
            var before = _observation;
            var step = _simulation.Apply(new GameAction(_observation.NextActionSequence, actionType, rewardId));
            if (!step.Accepted) { _message = "행동 거부: " + step.RejectionCode; RefreshInterface(); return; }
            _observation = step.Observation;
            _message = _observation.Phase == RunPhase.Terminal ? "도전이 끝났습니다. 아래에서 결과와 기록 상태를 확인하세요."
                : _observation.Phase == RunPhase.RewardChoice ? "상대를 제압했습니다. 보상 하나를 선택하세요."
                : actionType == GameActionType.ChooseReward ? "강화가 적용되었습니다. 다음 상대의 행동을 확인하세요."
                : ArenaText.ActionName(actionType) + " 사용 · " + (_observation.Turn != before.Turn ? "상대 행동 후 새 턴이 시작되었습니다." : "남은 행동력을 사용할 수 있습니다.");
            SaveProgress(); RefreshInterface(); _view.Feedback(before, _observation);
        }
        private void SaveProgress()
        {
            if (_smokeMode || _simulation == null || _context == null) return;
            try { _replayStore.Save(_context, _simulation.ActionLog, _activeTicket); }
            catch (Exception ex) { Debug.LogWarning("Replay save failed: " + ex.Message); _message = "로컬 저장에 실패했습니다. 게임을 종료하기 전에 저장 공간을 확인하세요."; }
        }
        private void RefreshInterface()
        {
            if (_view == null || _observation == null) return;
            var debug = new StringBuilder($"SEED {_context.BaseSeed}\nGAME {_config.GameVersion}\nCONFIG {_config.Checksum}\nSCORE {_scoreRule.Checksum}");
            var result = _observation.Phase == RunPhase.Terminal ? _simulation.GetCanonicalResult() : null;
            if (result != null) debug.Append("\nRESULT ").Append(result.ResultHash);
            debug.Append("\n최근 행동 (전체 ").Append(_simulation.ActionLog.Count).Append(")");
            foreach (var action in _simulation.ActionLog.Skip(Math.Max(0, _simulation.ActionLog.Count - 6)))
                debug.Append("\n#").Append(action.Sequence).Append(' ').Append(ArenaText.ActionName(action.ActionType)).Append(' ').Append(action.RewardId);
            _view.Render(_config, _observation, _simulation.ActionLog, result, _activeTicket != null, _message, _onlineStatus,
                _submissionStatus, _verified, _online.Busy, debug.ToString());
        }
        private void BuildInterface()
        {
            var asset = Resources.Load<PanelSettings>("SimOpsPanelSettings");
            if (asset == null) throw new InvalidOperationException("Runtime PanelSettings asset is missing.");
            var settings = Instantiate(asset); settings.name = "SimOps Runtime Panel";
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize; settings.referenceResolution = new Vector2Int(1600, 900);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; settings.match = 0.5f;
            // Hidden Windows swap chains can return black screenshots. Preview the same runtime
            // panel into a GPU texture instead; normal gameplay still renders to the display.
            if (_uiPreview)
            {
                _previewTexture = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
                _previewTexture.Create(); settings.targetTexture = _previewTexture; settings.clearColor = true;
            }
            var document = gameObject.AddComponent<UIDocument>(); document.panelSettings = settings;
            _safeAreaRoot = document.rootVisualElement; _safeAreaRoot.name = "simops-root";
            _view = new ArenaView(_safeAreaRoot, Perform, () => RequestNewRun(false), () => RequestNewRun(true), SubmitOnline,
                () => StartCoroutine(_online.Leaderboard(_activeTicket?.context?.seasonId)), ReplayCurrentLog);
            _view.RewardSelected += id => Perform(GameActionType.ChooseReward, id);
            _view.DialogClosed += RefreshInterface;
            _safeAreaRoot.RegisterCallback<GeometryChangedEvent>(_ => _view.Resize());
            ApplySafeAreaIfNeeded(true);
        }
        private void ApplySafeAreaIfNeeded(bool force = false)
        {
            if (_safeAreaRoot == null) return;
            var size = new Vector2Int(Screen.width, Screen.height); var safe = Screen.safeArea;
            if (!force && size == _lastScreenSize && safe == _lastSafeArea) return;
            var scale = Mathf.Max(.01f, Mathf.Lerp(Screen.width / 1600f, Screen.height / 900f, .5f));
            _safeAreaRoot.style.paddingLeft = safe.xMin / scale; _safeAreaRoot.style.paddingRight = (Screen.width - safe.xMax) / scale;
            _safeAreaRoot.style.paddingTop = safe.yMin / scale; _safeAreaRoot.style.paddingBottom = (Screen.height - safe.yMax) / scale;
            _lastScreenSize = size; _lastSafeArea = safe; _view?.Resize();
        }
        private void AcceptTicket(OnlineTicketData ticket)
        {
            GameConfig received;
            try { received = ticket.config?.ToConfig(); } catch (ArgumentException) { OnOnlineStatus("설정 검증에 실패했습니다."); return; }
            var context = ticket.context;
            if (received == null || context == null || context.gameVersion != received.GameVersion || context.configChecksum != received.Checksum ||
                context.scoreRuleVersion != _scoreRule.Version || context.scoreRuleChecksum != _scoreRule.Checksum ||
                context.gameCoreChecksum != _online.CoreChecksum || !ulong.TryParse(context.baseSeed, out var seed))
            { OnOnlineStatus("이 실행 파일이 지원하지 않는 게임 또는 설정 버전입니다."); return; }
            StartNewRun(seed, received); _activeTicket = ticket;
            _onlineStatus = "랭킹 도전 · 게임이 끝나면 결과 화면에서 제출하세요.";
            SaveProgress(); RefreshInterface();
        }
        private void SubmitOnline()
        {
            if (_activeTicket == null || _observation.Phase != RunPhase.Terminal || _online.Busy) return;
            StartCoroutine(SubmitWithFeedback());
        }
        private IEnumerator SubmitWithFeedback()
        {
            _submissionStatus = "전송·서버 검증 중… 창을 닫아도 같은 기록을 다시 확인할 수 있습니다.";
            RefreshInterface();
            yield return _online.Submit(_activeTicket, _simulation.ActionLog, _simulation.GetCanonicalResult().ResultHash, success => _verified = success);
            _submissionStatus = _verified ? "서버 검증 완료 · 시즌 랭킹에서 반영 여부와 개인 최고 기록을 확인하세요."
                : "검증 완료를 확인하지 못했습니다. 아래 안내를 확인하고 같은 기록으로 재시도하세요.";
            RefreshInterface();
        }
        private IEnumerator RunSmokeTest()
        {
            yield return null; yield return null;
            Perform(GameActionType.Strike);
            if (_view == null || _observation == null || _simulation.ActionLog.Count != 1) { Debug.LogError("SIMOPS_PLAYER_SMOKE_FAIL"); Application.Quit(1); yield break; }
            Debug.Log("SIMOPS_PLAYER_SMOKE_PASS interface=ready actionCount=1"); Application.Quit(0);
        }
        private IEnumerator RunOnlineSmokeTest()
        {
            yield return null; yield return _online.Begin(AcceptTicket);
            if (_activeTicket == null) { Debug.LogError("SIMOPS_ONLINE_SMOKE_FAIL ticket: " + _online.LastError); Application.Quit(1); yield break; }
            var count = 0;
            while (_observation.Phase != RunPhase.Terminal && count++ < 10000) AutoAction();
            yield return SubmitWithFeedback();
            OnlineRankingData ranking = null;
            yield return _online.Leaderboard(_activeTicket.context.seasonId, data => ranking = data);
            if (!_verified || ranking?.currentPlayer == null || ranking.currentPlayer.runId != _activeTicket.runId || !_view.SubmissionText.Contains("서버 검증 완료"))
            { Debug.LogError("SIMOPS_ONLINE_SMOKE_FAIL verification, UI status or ranking"); Application.Quit(1); yield break; }
            Debug.Log("SIMOPS_ONLINE_SMOKE_PASS run=" + _activeTicket.runId + " rank=" + ranking.currentPlayer.rank);
            Application.Quit(0);
        }
        private void AutoAction()
        {
            if (_observation.Phase == RunPhase.RewardChoice) Perform(GameActionType.ChooseReward, _observation.OfferedRewardIds[0]);
            else if (_observation.Player.CurrentHealth * 3 <= _observation.Player.MaxHealth && IsValid(GameActionType.UseItem)) Perform(GameActionType.UseItem);
            else if (_observation.Enemy?.Intent == EnemyIntentType.HeavyAttack && IsValid(GameActionType.Guard)) Perform(GameActionType.Guard);
            else if (IsValid(GameActionType.Technique)) Perform(GameActionType.Technique);
            else if (IsValid(GameActionType.Strike)) Perform(GameActionType.Strike);
            else Perform(GameActionType.EndTurn);
        }
        private IEnumerator CaptureUi(string name)
        {
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Debug.Log($"SIMOPS_UI_LAYOUT root={_safeAreaRoot.worldBound} scroll={_safeAreaRoot.Q<ScrollView>().worldBound}");
            if (!_view.CriticalControlsVisible(_observation.Phase))
            { Debug.LogError("SIMOPS_UI_FAIL clipped critical controls: " + name); Application.Quit(1); yield break; }
            var texture = new Texture2D(_previewTexture.width, _previewTexture.height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = _previewTexture;
            texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); texture.Apply();
            RenderTexture.active = previous;
            var pixels = texture.GetPixels32();
            var bright = pixels.Count(pixel => pixel.r > 100 && pixel.g > 100 && pixel.b > 100);
            if (bright < pixels.Length / 1000)
            { Destroy(texture); Debug.LogError("SIMOPS_UI_FAIL blank capture: " + name); Application.Quit(1); yield break; }
            var folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "ui-preview"); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name + "-" + Screen.width + "x" + Screen.height + ".png");
            File.WriteAllBytes(path, texture.EncodeToPNG()); Destroy(texture);
            Debug.Log("SIMOPS_UI_CAPTURE " + path + " brightPixels=" + bright);
        }
        private IEnumerator RunUiPreview()
        {
            yield return new WaitForSecondsRealtime(1f);
            yield return CaptureUi("battle");
            Perform(GameActionType.Strike); yield return CaptureUi("feedback");
            if (_view.IsActionEnabled(GameActionType.Technique)) { Debug.LogError("SIMOPS_UI_FAIL AP disable"); Application.Quit(1); yield break; }
            RequestNewRun(false);
            var heldActions = _simulation.ActionLog.Count;
            Perform(GameActionType.Strike);
            if (_simulation.ActionLog.Count != heldActions || _view.IsActionEnabled(GameActionType.Strike))
            { Debug.LogError("SIMOPS_UI_FAIL modal input guard"); Application.Quit(1); yield break; }
            _view.CancelConfirm();
            if (!_view.IsActionEnabled(GameActionType.Strike)) { Debug.LogError("SIMOPS_UI_FAIL modal cancel"); Application.Quit(1); yield break; }
            var count = 0;
            while (_observation.Phase == RunPhase.PlayerTurn && count++ < 10000) AutoAction();
            if (_observation.Phase != RunPhase.RewardChoice || !_view.HasPhase(RunPhase.RewardChoice)) { Debug.LogError("SIMOPS_UI_FAIL reward"); Application.Quit(1); yield break; }
            yield return CaptureUi("reward");
            while (_observation.Phase != RunPhase.Terminal && count++ < 10000) AutoAction();
            if (_observation.Outcome != RunOutcome.Victory || !_view.HasPhase(RunPhase.Terminal)) { Debug.LogError("SIMOPS_UI_FAIL victory"); Application.Quit(1); yield break; }
            yield return CaptureUi("victory");
            // Visual fixtures only. The isolated LiveOps test exercises real submission separately.
            _view.Render(_config, _observation, _simulation.ActionLog, _simulation.GetCanonicalResult(), true, "테스트 표시 · 서버 전송 없음", "", "결과 미제출 · 서버 검증을 요청하세요.", false, false, "");
            if (!_view.IsSubmitEnabled) { Debug.LogError("SIMOPS_UI_FAIL submit enabled"); Application.Quit(1); yield break; }
            yield return CaptureUi("ranked-unsubmitted-fixture");
            _view.Render(_config, _observation, _simulation.ActionLog, _simulation.GetCanonicalResult(), true, "테스트 표시 · 서버 전송 없음", "", "전송·서버 검증 중…", false, true, "");
            if (_view.IsSubmitEnabled) { Debug.LogError("SIMOPS_UI_FAIL busy submit"); Application.Quit(1); yield break; }
            yield return CaptureUi("ranked-pending-fixture");
            _view.Render(_config, _observation, _simulation.ActionLog, _simulation.GetCanonicalResult(), true, "테스트 표시 · 서버 전송 없음", "", "서버 검증 완료 · 시즌 랭킹에서 반영 여부를 확인하세요.", true, false, "");
            if (_view.IsSubmitEnabled) { Debug.LogError("SIMOPS_UI_FAIL verified submit"); Application.Quit(1); yield break; }
            yield return CaptureUi("ranked-verified-fixture"); RefreshInterface();
            RequestNewRun(false);
            if (!_view.ConfirmOpen) { Debug.LogError("SIMOPS_UI_FAIL confirmation"); Application.Quit(1); yield break; }
            yield return CaptureUi("confirm"); _view.CancelConfirm();
            StartNewRun(DefaultSeed);
            Perform(GameActionType.EndTurn); Perform(GameActionType.UseItem); yield return CaptureUi("heal");
            while (_observation.Phase != RunPhase.Terminal && count++ < 10000) Perform(GameActionType.EndTurn);
            if (_observation.Outcome != RunOutcome.Defeat) { Debug.LogError("SIMOPS_UI_FAIL defeat"); Application.Quit(1); yield break; }
            yield return CaptureUi("defeat");
            Debug.Log("SIMOPS_UI_PREVIEW_PASS battle,reward,victory,defeat,confirmation,heal,submission-fixtures,input-guards; saves=disabled network=unused");
            Application.Quit(0);
        }
        private static bool HasArgument(string argument) => Array.IndexOf(Environment.GetCommandLineArgs(), argument) >= 0;
    }
}
