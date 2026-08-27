using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using SimOps.Game.Core;
using SimOps.Game.Transport;
using UnityEngine;
using UnityEngine.Networking;

namespace SimOps.Unity
{
    public sealed class SimOpsOnlineClient : MonoBehaviour
    {
        private string ApiUrl = "http://127.0.0.1:5080";
        private string CredentialKey = "simops.local5080.playerCredential";
        private string _credential;
        private string _pendingBeginKey;
        private string _pendingBeginSeason;
        private bool _smokeSession;
        public bool Busy { get; private set; }
        public string LastError { get; private set; }
        public event Action<string> StatusChanged;

        public string CoreChecksum => Resources.Load<TextAsset>("SimOpsGameCoreChecksum")?.text.Trim() ?? string.Empty;

        private void Awake()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Web uses a same-origin /api reverse proxy. Never ship admin/DB keys to the browser.
            var page = new Uri(Application.absoluteURL);
            ApiUrl = page.GetLeftPart(UriPartial.Authority);
            CredentialKey = "simops.web.playerCredential." + new Uri(page, ".").AbsolutePath;
#endif
            _smokeSession = Array.IndexOf(Environment.GetCommandLineArgs(), "--simops-online-smoke") >= 0;
            var arguments = Environment.GetCommandLineArgs();
            var apiArgument = Array.IndexOf(arguments, "--simops-api-url");
            if (_smokeSession && apiArgument >= 0 && apiArgument + 1 < arguments.Length && arguments[apiArgument + 1] == "http://127.0.0.1:5081") ApiUrl = arguments[apiArgument + 1];
            _credential = _smokeSession ? string.Empty : PlayerPrefs.GetString(CredentialKey, string.Empty);
        }

        public IEnumerator Begin(Action<OnlineTicketData> ready)
        {
            if (Busy) yield break;
            Busy = true;
            try
            {
                if (CoreChecksum.Length != 64) { Report("게임 버전 정보가 없습니다. 실행 파일을 다시 빌드해 주세요."); yield break; }
                if (string.IsNullOrEmpty(_credential))
                {
                    OnlineCredentialData registration = null;
                    yield return Request("POST", "/api/v1/player/register", new RegisterData { requestedNickname = "Player-" + Guid.NewGuid().ToString("N").Substring(0, 8) },
                        json => registration = JsonUtility.FromJson<OnlineCredentialData>(json));
                    if (registration == null) yield break;
                    _credential = registration.credential;
                    if (!_smokeSession)
                    {
                        PlayerPrefs.SetString(CredentialKey, _credential);
                        PlayerPrefs.Save();
                    }
                }
                OnlineSeasonData season = null;
                yield return Request("GET", "/api/v1/public/seasons/active", null, json => season = JsonUtility.FromJson<OnlineSeasonData>(json));
                if (season == null) yield break;
                if (_pendingBeginKey == null || _pendingBeginSeason != season.seasonId)
                {
                    _pendingBeginKey = Guid.NewGuid().ToString("N");
                    _pendingBeginSeason = season.seasonId;
                }
                OnlineTicketData ticket = null;
                yield return Request("POST", "/api/v1/player/tickets", new BeginData
                {
                    seasonId = season.seasonId, clientGameCoreChecksum = CoreChecksum, idempotencyKey = _pendingBeginKey,
                }, json => ticket = JsonUtility.FromJson<OnlineTicketData>(json));
                if (ticket == null) yield break;
                if (DateTimeOffset.TryParse(ticket.expiresAt, out var expiry) && expiry <= DateTimeOffset.UtcNow)
                {
                    _pendingBeginKey = null;
                    Report("랭킹 도전 유효 시간이 지났습니다. 새 랭킹 도전을 시작해 주세요.");
                    yield break;
                }
                PublishedConfig config = null;
                yield return Request("GET", "/api/v1/public/seasons/" + ticket.context.seasonId + "/config", null,
                    json => config = JsonUtility.FromJson<PublishedConfig>(json));
                if (config == null) yield break;
                ticket.config = config;
                ticket.submitKey = Guid.NewGuid().ToString("N");
                _pendingBeginKey = null;
                Report("랭킹 도전 준비 완료. 결과는 한 판이 끝난 뒤 제출할 수 있습니다.");
                ready(ticket);
            }
            finally { Busy = false; }
        }

        public IEnumerator Submit(OnlineTicketData ticket, IReadOnlyList<GameAction> actions, string resultHash, Action<bool> completed = null)
        {
            if (Busy || ticket == null) yield break;
            Busy = true;
            try
            {
                var body = new SubmitData
                {
                    runTicket = ticket.runTicket, idempotencyKey = ticket.submitKey, clientGameCoreChecksum = CoreChecksum,
                    actionLogSchemaVersion = 1, clientResultHash = resultHash, actions = new List<ReplayActionData>(),
                };
                foreach (var action in actions) body.actions.Add(new ReplayActionData
                {
                    sequence = action.Sequence, actionType = (int)action.ActionType, rewardId = action.RewardId,
                });
                OnlineReceiptData receipt = null;
                yield return Request("POST", "/api/v1/player/runs", body, json => receipt = JsonUtility.FromJson<OnlineReceiptData>(json));
                if (receipt == null) { completed?.Invoke(false); yield break; }
                Report("서버 접수 완료 · 행동 로그를 재실행해 검증하고 있습니다.");
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    OnlineRunStatus status = null;
                    yield return Request("GET", "/api/v1/player/runs/" + receipt.runId, null, json => status = JsonUtility.FromJson<OnlineRunStatus>(json));
                    if (status == null) { completed?.Invoke(false); yield break; }
                    if (status.status == "verified")
                    {
                        Report("서버 검증 완료 · 점수 " + status.result.finalScore.ToString("N0"));
                        completed?.Invoke(true);
                        yield break;
                    }
                    if (status.status == "rejected" || status.status == "failed")
                    {
                        Report("서버 검증 실패 · " + status.rejectionCode);
                        completed?.Invoke(false);
                        yield break;
                    }
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                Report("아직 검증 대기 중입니다. 결과 버튼을 다시 누르면 같은 기록의 상태를 확인합니다.");
                completed?.Invoke(false);
            }
            finally { Busy = false; }
        }

        public IEnumerator Leaderboard(string seasonId = null, Action<OnlineRankingData> received = null)
        {
            if (Busy) yield break;
            Busy = true;
            try
            {
                if (string.IsNullOrEmpty(seasonId))
                {
                    OnlineSeasonData season = null;
                    yield return Request("GET", "/api/v1/public/seasons/active", null, json => season = JsonUtility.FromJson<OnlineSeasonData>(json));
                    if (season == null) yield break;
                    seasonId = season.seasonId;
                }
                OnlineRankingData ranking = null;
                yield return Request("GET", "/api/v1/public/seasons/" + seasonId + "/leaderboard?limit=3", null,
                    json => ranking = JsonUtility.FromJson<OnlineRankingData>(json));
                if (ranking == null) yield break;
                var text = new StringBuilder((ranking.status == "active" ? "진행 중 시즌" : "종료 시즌") + " · 검증된 플레이어 " + ranking.totalPlayers + "명");
                foreach (var entry in ranking.entries) text.Append("\n").Append(entry.rank).Append("위  ").Append(entry.nickname).Append(" — ").Append(entry.score).Append("점");
                if (ranking.currentPlayer != null && !string.IsNullOrEmpty(ranking.currentPlayer.playerId))
                    text.Append("\n내 최고 기록  ").Append(ranking.currentPlayer.rank).Append("위 — ").Append(ranking.currentPlayer.score).Append("점");
                else text.Append("\n아직 이 시즌에 등록된 내 최고 기록이 없습니다.");
                Report(text.ToString());
                received?.Invoke(ranking);
            }
            finally { Busy = false; }
        }

        private IEnumerator Request(string method, string path, object body, Action<string> success)
        {
            LastError = null;
            using (var request = new UnityWebRequest(ApiUrl + path, method))
            {
                request.timeout = 15;
                request.downloadHandler = new DownloadHandlerBuffer();
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                if (!string.IsNullOrEmpty(_credential)) request.SetRequestHeader("Authorization", "Bearer " + _credential);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    LastError = "API " + request.responseCode + ": " + (string.IsNullOrEmpty(request.downloadHandler.text) ? request.error : request.downloadHandler.text);
                    var code = "";
                    try { code = JsonUtility.FromJson<OnlineErrorData>(request.downloadHandler.text)?.code ?? ""; } catch (ArgumentException) { }
                    Report(request.responseCode == 0 ? "서버에 연결할 수 없습니다. 로컬 API 실행과 네트워크를 확인한 뒤 재시도하세요."
                        : code == "TICKET_EXPIRED" ? "랭킹 도전 유효 시간이 지났습니다. 기록을 제출할 수 없습니다."
                        : code == "SEASON_NOT_ACTIVE" ? "시즌이 종료되어 이 기록을 제출할 수 없습니다."
                        : $"서버 요청 실패 ({request.responseCode}) · {code}");
                    yield break;
                }
                success(request.downloadHandler.text);
            }
        }

        private void Report(string text) { StatusChanged?.Invoke(text); }
    }

    [Serializable] public sealed class OnlineTicketData { public string runId; public string runTicket; public string submitKey; public OnlineContextData context; public string expiresAt; public PublishedConfig config; }
    [Serializable] public sealed class OnlineContextData
    {
        public string seasonId; public string gameVersion; public string gameCoreChecksum; public string configChecksum;
        public string scoreRuleVersion; public string scoreRuleChecksum; public string baseSeed;
    }
    [Serializable] public sealed class OnlineSeasonData { public string seasonId; }
    [Serializable] public sealed class OnlineCredentialData { public string playerId; public string credential; public string normalizedNickname; }
    [Serializable] internal sealed class OnlineErrorData { public string code; }
    [Serializable] public sealed class OnlineReceiptData { public string runId; public string status; }
    [Serializable] public sealed class OnlineRunStatus { public string status; public string rejectionCode; public OnlineResultData result; }
    [Serializable] public sealed class OnlineResultData { public int finalScore; }
    [Serializable] public sealed class OnlineRankingData { public string status; public long totalPlayers; public OnlineRankEntry[] entries; public OnlineRankEntry currentPlayer; }
    [Serializable] public sealed class OnlineRankEntry { public long rank; public string playerId; public string nickname; public string runId; public int score; }
    [Serializable] internal sealed class RegisterData { public string requestedNickname; }
    [Serializable] internal sealed class BeginData { public string seasonId; public string clientGameCoreChecksum; public string idempotencyKey; }
    [Serializable] internal sealed class SubmitData
    {
        public string runTicket; public string idempotencyKey; public string clientGameCoreChecksum; public int actionLogSchemaVersion;
        public string clientResultHash; public List<ReplayActionData> actions;
    }
}
