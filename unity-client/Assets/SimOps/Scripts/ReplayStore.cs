using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SimOps.Game.Core;
using UnityEngine;

namespace SimOps.Unity
{
    public sealed class ReplayStore
    {
        private const string FileName = "simops-replay.json";

        public string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public void Save(
            RunContext context,
            IReadOnlyList<GameAction> actions,
            OnlineTicketData ticket = null)
        {
            var data = new ReplayData
            {
                gameVersion = context.GameVersion,
                configChecksum = context.ConfigChecksum,
                scoreRuleVersion = context.ScoreRuleVersion,
                scoreRuleChecksum = context.ScoreRuleChecksum,
                baseSeed = context.BaseSeed.ToString(CultureInfo.InvariantCulture),
                actions = new List<ReplayActionData>(actions.Count),
                onlineTicket = ticket,
            };

            for (var index = 0; index < actions.Count; index++)
            {
                var action = actions[index];
                data.actions.Add(new ReplayActionData
                {
                    sequence = action.Sequence,
                    actionType = (int)action.ActionType,
                    rewardId = action.RewardId ?? string.Empty,
                });
            }

            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, true));
            File.Copy(temporaryPath, FilePath, true);
            File.Delete(temporaryPath);
        }

        public bool TryLoad(out ReplayData data)
        {
            data = null;
            if (!File.Exists(FilePath))
            {
                return false;
            }

            try
            {
                data = JsonUtility.FromJson<ReplayData>(File.ReadAllText(FilePath));
                return data != null && data.actions != null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Replay load failed: {exception.Message}");
                return false;
            }
        }
    }

    [Serializable]
    public sealed class ReplayData
    {
        public string gameVersion;
        public string configChecksum;
        public string scoreRuleVersion;
        public string scoreRuleChecksum;
        public string baseSeed;
        public List<ReplayActionData> actions;
        public OnlineTicketData onlineTicket;
    }

    [Serializable]
    public sealed class ReplayActionData
    {
        public int sequence;
        public int actionType;
        public string rewardId;
    }
}
