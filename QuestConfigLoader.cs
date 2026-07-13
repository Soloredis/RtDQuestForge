using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using Newtonsoft.Json;

namespace RtDQuestForge
{
    public static class QuestConfigLoader
    {
        private const string DefaultFileName = "quests_default.json";

        public static QuestList LoadAll(string configDir, ManualLogSource logger)
        {
            Directory.CreateDirectory(configDir);
            EnsureDefaultFileExists(configDir, logger);

            // Every quest json in the folder gets loaded as its own journal
            // page, including the bundled starter file. Progress files are
            // player save data, not quest definitions, so they are skipped.
            string[] filesToLoad = Directory.GetFiles(configDir, "*.json")
                .Where(f => !Path.GetFileName(f).StartsWith("progress_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            QuestList combined = new QuestList();
            HashSet<string> seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string file in filesToLoad)
            {
                MergeFile(file, combined, seenIds, logger);
            }

            logger.LogMessage("Loaded " + combined.Quests.Count + " quest(s) from " + filesToLoad.Length + " file(s).");
            return combined;
        }

        private static void MergeFile(string file, QuestList combined, HashSet<string> seenIds, ManualLogSource logger)
        {
            string fileName = Path.GetFileName(file);
            string raw;

            try
            {
                raw = File.ReadAllText(file);
            }
            catch (IOException ex)
            {
                logger.LogWarning($"Exception caught while reading {fileName}: {ex}");
                return;
            }

            QuestList parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<QuestList>(raw);
            }
            catch (JsonException ex)
            {
                logger.LogWarning($"Exception caught while parsing {fileName}, file was skipped: {ex}");
                return;
            }

            if (parsed == null || parsed.Quests == null) return;

            foreach (QuestConfig quest in parsed.Quests)
            {
                if (string.IsNullOrWhiteSpace(quest.ID))
                {
                    logger.LogWarning("Skipped a quest with no ID in " + fileName + " ('" + quest.Title + "').");
                    continue;
                }

                if (!seenIds.Add(quest.ID))
                {
                    logger.LogWarning("Duplicate quest ID '" + quest.ID + "' in " + fileName + " was skipped, already loaded from another file.");
                    continue;
                }

                quest.SourceFile = Path.GetFileNameWithoutExtension(file);
                combined.Quests.Add(quest);
            }
        }

        private static void EnsureDefaultFileExists(string configDir, ManualLogSource logger)
        {
            try
            {
                string path = Path.Combine(configDir, DefaultFileName);
                if (File.Exists(path)) return;

                // Example 1: kill objective with an item reward
                QuestConfig killQuest = new QuestConfig();
                killQuest.ID = "example_kill_quest";
                killQuest.Title = "Thin the Herd";
                killQuest.Goal = "A few deer kills to get your bearings with the quest journal.";
                killQuest.Rarity = "Common";
                killQuest.KillReqs = new List<ObjectiveEntry> { new ObjectiveEntry { Prefab = "Deer", Amount = 3 } };
                killQuest.RewardItems = new List<RewardEntry> { new RewardEntry { Prefab = "Coins", Amount = 20 } };

                // Example 2: gather objective with multiple item rewards
                QuestConfig gatherQuest = new QuestConfig();
                gatherQuest.ID = "example_gather_quest";
                gatherQuest.Title = "Supply Run";
                gatherQuest.Goal = "Gather wood and stone for the settlement.";
                gatherQuest.Rarity = "Common";
                gatherQuest.GatherReqs = new List<ObjectiveEntry>
                {
                    new ObjectiveEntry { Prefab = "Wood", Amount = 10 },
                    new ObjectiveEntry { Prefab = "Stone", Amount = 5 }
                };
                gatherQuest.RewardItems = new List<RewardEntry>
                {
                    new RewardEntry { Prefab = "Coins", Amount = 15 },
                    new RewardEntry { Prefab = "Resin", Amount = 5 }
                };

                // Example 3: mixed objectives, skill XP reward, and a PreReqID chain
                QuestConfig skillQuest = new QuestConfig();
                skillQuest.ID = "example_skill_quest";
                skillQuest.PreReqID = "example_kill_quest";
                skillQuest.Title = "Seasoned Hunter";
                skillQuest.Goal = "Prove yourself against the boars and bring back their leather.";
                skillQuest.Rarity = "Uncommon";
                skillQuest.KillReqs = new List<ObjectiveEntry> { new ObjectiveEntry { Prefab = "Boar", Amount = 5 } };
                skillQuest.GatherReqs = new List<ObjectiveEntry> { new ObjectiveEntry { Prefab = "LeatherScraps", Amount = 5 } };
                skillQuest.RewardItems = new List<RewardEntry> { new RewardEntry { Prefab = "Coins", Amount = 40 } };
                skillQuest.SkillRewards = new List<SkillRewardEntry>
                {
                    new SkillRewardEntry { Skill = "Bows", Amount = 10f },
                    new SkillRewardEntry { Skill = "Run", Amount = 5f }
                };

                // Example 4: EpicMMO XP reward (only granted if EpicMMOSystem is installed, ignored otherwise)
                QuestConfig mmoQuest = new QuestConfig();
                mmoQuest.ID = "example_mmo_quest";
                mmoQuest.Title = "Greydwarf Purge";
                mmoQuest.Goal = "Clear out the greydwarves lurking at the forest's edge.";
                mmoQuest.Rarity = "Common";
                mmoQuest.KillReqs = new List<ObjectiveEntry> { new ObjectiveEntry { Prefab = "Greydwarf", Amount = 5 } };
                mmoQuest.RewardItems = new List<RewardEntry> { new RewardEntry { Prefab = "Coins", Amount = 25 } };
                mmoQuest.ExpReward = 150;

                QuestList starterPack = new QuestList();
                starterPack.Quests = new List<QuestConfig> { killQuest, gatherQuest, skillQuest, mmoQuest };

                File.WriteAllText(path, JsonConvert.SerializeObject(starterPack, Formatting.Indented));
                logger.LogMessage("Created starter quest file at " + path);
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Exception caught while creating the default quest file: {ex}");
            }
        }
    }
}