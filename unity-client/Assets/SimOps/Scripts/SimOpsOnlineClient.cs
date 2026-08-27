using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using SimOps.Game.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace SimOps.Unity
{
    public sealed class SimOpsOnlineClient : MonoBehaviour
    {
        private const string ApiUrl = "http://127.0.0.1:5080";
        private const string CredentialKey = "simops.local5080.playerCredential";
        private string _credential;
        private string _pendingBeginKey;
        private bool _smokeSession;
        public bool Busy { get; private set; }
        public string LastError { get; private set; }
        public event Action<string> StatusChanged;

        public string CoreChecksum => Resources.Load<TextAsset>("SimOpsGameCoreChecksum")?.text.Trim() ?? string.Empty;

        private void Awake()
        {
            _smokeSession = Array.IndexOf(Environment.GetCommandLineArgs(), "--simops-online-smoke") >= 0;
            _credential = _smokeSession ? string.Empty : PlayerPrefs.GetString(CredentialKey, string.Empty);
        }

        public IEnumerator Begin(Action<OnlineTicketData> ready)
        {
            if (Busy) yield break;
            Busy = true;
            try
            {
                if (CoreChecksum.Length != 64) { Report("Missing packaged Game Core checksum."); yield break; }
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
                if (_pendingBeginKey == null) _pendingBeginKey = Guid.NewGuid().ToString("N");
                OnlineTicketData ticket = null;
                yield return Request("POST", "/api/v1/player/tickets", new BeginData
                {
                    seasonId = season.seasonId, clientGameCoreChecksum = CoreChecksum, idempotencyKey = _pendingBeginKey,
                }, json => ticket = JsonUtility.FromJson<OnlineTicketData>(json));
                if (ticket == null) yield break;
                ticket.submitKey = Guid.NewGuid().ToString("N");
                _pendingBeginKey = null;
                Report("Ranked run ready. Ticket expires: " + ticket.expiresAt);
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
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    OnlineRunStatus status = null;
                    yield return Request("GET", "/api/v1/player/runs/" + receipt.runId, null, json => status = JsonUtility.FromJson<OnlineRunStatus>(json));
                    if (status == null) { completed?.Invoke(false); yield break; }
                    if (status.status == "verified")
                    {
                        Report("Server verified your run. Score: " + status.result.finalScore);
                        completed?.Invoke(true);
                        yield break;
                    }
                    if (status.status == "rejected" || status.status == "failed")
                    {
                        Report(status.status + ": " + status.rejectionCode);
                        completed?.Invoke(false);
                        yield break;
                    }
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                Report("Verification is still pending. Submit again to check the same run.");
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
                var text = new StringBuilder("SEASON " + ranking.status + " | verified players " + ranking.totalPlayers);
                foreach (var entry in ranking.entries) text.Append("\n#").Append(entry.rank).Append(" ").Append(entry.nickname).Append(" — ").Append(entry.score);
                if (ranking.currentPlayer != null && !string.IsNullOrEmpty(ranking.currentPlayer.playerId))
                    text.Append("\nYOU #").Append(ranking.currentPlayer.rank).Append(" — ").Append(ranking.currentPlayer.score);
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
                    Report(LastError);
                    yield break;
                }
                success(request.downloadHandler.text);
            }
        }

        private void Report(string text) { StatusChanged?.Invoke(text); }
    }

    [Serializable] public sealed class OnlineTicketData { public string runId; public string runTicket; public string submitKey; public OnlineContextData context; public string expiresAt; }
    [Serializable] public sealed class OnlineContextData
    {
        public string seasonId; public string gameVersion; public string gameCoreChecksum; public string configChecksum;
        public string scoreRuleVersion; public string scoreRuleChecksum; public string baseSeed;
    }
    [Serializable] public sealed class OnlineSeasonData { public string seasonId; }
    [Serializable] public sealed class OnlineCredentialData { public string playerId; public string credential; public string normalizedNickname; }
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
