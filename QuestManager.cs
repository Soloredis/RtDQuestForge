using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using Newtonsoft.Json;

namespace RtDQuestForge
{
    public class QuestManager
    {
        public QuestList AllQuests;

        public QuestProgressData Progress;

        public event Action<QuestConfig> OnQuestCompleted;

        public event Action<QuestConfig, string, int, int> OnObjectiveProgress;

        private readonly string ConfigDir;

        private readonly ManualLogSource Logger;

        private string ProgressFilePath;

        public QuestManager(string configDir, ManualLogSource logger)
        {
            ConfigDir = configDir;
            Logger = logger;
            AllQuests = new QuestList();
            Progress = new QuestProgressData();
        }

        public void LoadQuestDefinitions()
        {
            try
            {
                AllQuests = QuestConfigLoader.LoadAll(ConfigDir, Logger);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while loading quest definitions: {ex}");
            }
        }

        // Clients call this after receiving the server's authoritative quest list over RPC.
        public void ApplyServerQuestList(QuestList list)
        {
            AllQuests = list != null ? list : new QuestList();
        }

        public void LoadProgress(string characterName)
        {
            try
            {
                ProgressFilePath = Path.Combine(ConfigDir, "progress_" + SanitizeFileName(characterName) + ".json");

                if (File.Exists(ProgressFilePath))
                {
                    Progress = JsonConvert.DeserializeObject<QuestProgressData>(File.ReadAllText(ProgressFilePath));

                    if (Progress == null)
                    {
                        Progress = new QuestProgressData();
                    }

                    return;
                }

                Progress = new QuestProgressData();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while loading progress file, starting fresh: {ex}");
                Progress = new QuestProgressData();
            }
        }

        public void SaveProgress()
        {
            try
            {
                if (string.IsNullOrEmpty(ProgressFilePath)) return;

                File.WriteAllText(ProgressFilePath, JsonConvert.SerializeObject(Progress, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while saving progress file: {ex}");
            }
        }

        public IEnumerable<QuestConfig> GetAvailableQuests()
        {
            return AllQuests.Quests.Where(q =>
                !Progress.CompletedQuestIDs.Contains(q.ID) &&
                (string.IsNullOrEmpty(q.PreReqID) || Progress.CompletedQuestIDs.Contains(q.PreReqID)));
        }

        // NEW: the player takes on a quest. Only accepted quests gain progress.
        public void AcceptQuest(QuestConfig quest)
        {
            Progress.AcceptedQuestIDs.Add(quest.ID);

            // Accepted quests are shown on the HUD tracker automatically.
            Progress.TrackedQuestIDs.Add(quest.ID);

            SaveProgress();
        }

        // NEW: the player drops a quest. Progress for it is wiped so
        // re-accepting later starts fresh at zero.
        public void AbandonQuest(QuestConfig quest)
        {
            Progress.AcceptedQuestIDs.Remove(quest.ID);
            Progress.TrackedQuestIDs.Remove(quest.ID);

            foreach (ObjectiveEntry kill in quest.KillReqs)
            {
                Progress.KillCounts.Remove(quest.ID + ":" + kill.Prefab);
            }

            foreach (ObjectiveEntry gather in quest.GatherReqs)
            {
                Progress.GatherCounts.Remove(quest.ID + ":" + gather.Prefab);
            }

            SaveProgress();
        }

        public void RegisterKill(string prefabName)
        {
            RegisterObjective(prefabName, q => q.KillReqs, Progress.KillCounts, 1);
        }

        public void RegisterGather(string prefabName, int amountAdded)
        {
            RegisterObjective(prefabName, q => q.GatherReqs, Progress.GatherCounts, amountAdded);
        }

        private void RegisterObjective(string prefabName, Func<QuestConfig, List<ObjectiveEntry>> selector, Dictionary<string, int> counters, int amount)
        {
            try
            {
                bool anyProgress = false;

                // CHANGED: only quests the player has accepted gain progress.
                foreach (QuestConfig quest in GetAvailableQuests().Where(q => Progress.AcceptedQuestIDs.Contains(q.ID)).ToList())
                {
                    List<ObjectiveEntry> objectives = selector(quest);
                    if (objectives == null) continue;

                    ObjectiveEntry objective = objectives.FirstOrDefault(o => string.Equals(o.Prefab, prefabName, StringComparison.OrdinalIgnoreCase));
                    if (objective == null) continue;

                    string counterKey = quest.ID + ":" + prefabName;
                    int current;
                    counters.TryGetValue(counterKey, out current);
                    current = Math.Min(current + amount, objective.Amount);
                    counters[counterKey] = current;
                    anyProgress = true;

                    if (OnObjectiveProgress != null)
                    {
                        OnObjectiveProgress(quest, prefabName, current, objective.Amount);
                    }

                    if (IsQuestComplete(quest))
                    {
                        CompleteQuest(quest);
                    }
                }

                if (anyProgress)
                {
                    SaveProgress();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while registering objective progress for {prefabName}: {ex}");
            }
        }

        private bool IsQuestComplete(QuestConfig quest)
        {
            bool killsDone = quest.KillReqs.All(o =>
            {
                int c;
                return Progress.KillCounts.TryGetValue(quest.ID + ":" + o.Prefab, out c) && c >= o.Amount;
            });

            bool gatherDone = quest.GatherReqs.All(o =>
            {
                int c;
                return Progress.GatherCounts.TryGetValue(quest.ID + ":" + o.Prefab, out c) && c >= o.Amount;
            });

            return killsDone && gatherDone;
        }

        private void CompleteQuest(QuestConfig quest)
        {
            Progress.CompletedQuestIDs.Add(quest.ID);
            Progress.TrackedQuestIDs.Remove(quest.ID);

            // CHANGED: a finished quest is no longer in the accepted list.
            Progress.AcceptedQuestIDs.Remove(quest.ID);

            if (OnQuestCompleted != null)
            {
                OnQuestCompleted(quest);
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name;
        }
    }
}