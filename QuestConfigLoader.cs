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

        // Small local helper so each quest below reads as a single block instead
        // of a dozen repeated property assignments. Any list left null just
        // falls back to the empty defaults on QuestConfig.
        private static QuestConfig MakeQuest(
            string id,
            string title,
            string goal,
            string rarity,
            List<ObjectiveEntry> kills = null,
            List<ObjectiveEntry> gathers = null,
            List<RewardEntry> rewards = null,
            List<SkillRewardEntry> skills = null,
            int expReward = 0,
            string preReqID = null)
        {
            QuestConfig q = new QuestConfig();
            q.ID = id;
            q.Title = title;
            q.Goal = goal;
            q.Rarity = rarity;
            q.PreReqID = preReqID;
            if (kills != null) q.KillReqs = kills;
            if (gathers != null) q.GatherReqs = gathers;
            if (rewards != null) q.RewardItems = rewards;
            if (skills != null) q.SkillRewards = skills;
            q.ExpReward = expReward;
            return q;
        }

        private static ObjectiveEntry Obj(string prefab, int amount)
        {
            return new ObjectiveEntry { Prefab = prefab, Amount = amount };
        }

        private static RewardEntry Item(string prefab, int amount)
        {
            return new RewardEntry { Prefab = prefab, Amount = amount };
        }

        private static SkillRewardEntry Skill(string skill, float amount)
        {
            return new SkillRewardEntry { Skill = skill, Amount = amount };
        }

        private static void EnsureDefaultFileExists(string configDir, ManualLogSource logger)
        {
            try
            {
                string path = Path.Combine(configDir, DefaultFileName);
                if (File.Exists(path)) return;

                QuestList starterPack = new QuestList();
                List<QuestConfig> q = starterPack.Quests;

                // Meadows
                q.Add(MakeQuest(
                    "example_meadows_boar", "Boar Trouble",
                    "The boars are trampling the crops. Cull a few near the settlement.",
                    "Common",
                    kills: new List<ObjectiveEntry> { Obj("Boar", 6) },
                    rewards: new List<RewardEntry> { Item("Coins", 20), Item("LeatherScraps", 5) },
                    skills: new List<SkillRewardEntry> { Skill("Clubs", 5f) }));

                q.Add(MakeQuest(
                    "example_meadows_deer", "The Hunt Begins",
                    "Prove your bow. Bring down deer and carry back the meat.",
                    "Common",
                    kills: new List<ObjectiveEntry> { Obj("Deer", 5) },
                    gathers: new List<ObjectiveEntry> { Obj("DeerMeat", 4) },
                    rewards: new List<RewardEntry> { Item("Coins", 25), Item("ArrowWood", 25) },
                    skills: new List<SkillRewardEntry> { Skill("Bows", 8f) },
                    preReqID: "example_meadows_boar"));

                // BlackForest
                q.Add(MakeQuest(
                    "example_bf_greydwarf", "Into the Black Forest",
                    "The greydwarves swarm the treeline. Cut them down and collect their eyes.",
                    "Uncommon",
                    kills: new List<ObjectiveEntry> { Obj("Greydwarf", 10) },
                    gathers: new List<ObjectiveEntry> { Obj("GreydwarfEye", 8) },
                    rewards: new List<RewardEntry> { Item("Coins", 30), Item("Resin", 15) },
                    skills: new List<SkillRewardEntry> { Skill("Blocking", 6f) },
                    expReward: 120));

                q.Add(MakeQuest(
                    "example_bf_troll", "Troll Toll",
                    "A troll is tearing through the forest. Put it down and take its hide.",
                    "Rare",
                    kills: new List<ObjectiveEntry> { Obj("Troll", 1) },
                    gathers: new List<ObjectiveEntry> { Obj("TrollHide", 3) },
                    rewards: new List<RewardEntry> { Item("Coins", 75), Item("MeadHealthMinor", 3) },
                    skills: new List<SkillRewardEntry> { Skill("Axes", 10f) },
                    expReward: 250,
                    preReqID: "example_bf_greydwarf"));

                // Swamp
                q.Add(MakeQuest(
                    "example_swamp_draugr", "The Restless Dead",
                    "Draugr and blobs haunt the swamp. Clear them and gather entrails.",
                    "Rare",
                    kills: new List<ObjectiveEntry> { Obj("Draugr", 10), Obj("Blob", 6) },
                    gathers: new List<ObjectiveEntry> { Obj("Entrails", 5) },
                    rewards: new List<RewardEntry> { Item("Coins", 60), Item("ArrowIron", 30) },
                    skills: new List<SkillRewardEntry> { Skill("Spears", 10f) },
                    expReward: 300));

                q.Add(MakeQuest(
                    "example_swamp_iron", "Iron from the Muck",
                    "Dredge scrap iron from the crypts and recover withered bones.",
                    "Rare",
                    gathers: new List<ObjectiveEntry> { Obj("IronScrap", 10), Obj("WitheredBone", 3) },
                    rewards: new List<RewardEntry> { Item("Coins", 80), Item("ArrowIron", 40) },
                    preReqID: "example_swamp_draugr"));

                // Mountain
                q.Add(MakeQuest(
                    "example_mountain_wolf", "Cold Fangs",
                    "Wolves stalk the peaks. Hunt them and bring back pelts and fangs.",
                    "Epic",
                    kills: new List<ObjectiveEntry> { Obj("Wolf", 10) },
                    gathers: new List<ObjectiveEntry> { Obj("WolfPelt", 6), Obj("WolfFang", 3) },
                    rewards: new List<RewardEntry> { Item("Coins", 100), Item("MeadStaminaMinor", 4) },
                    skills: new List<SkillRewardEntry> { Skill("Bows", 12f) },
                    expReward: 400));

                q.Add(MakeQuest(
                    "example_mountain_silver", "Silver Veins",
                    "Face the mountain's elite draugr and mine obsidian and silver.",
                    "Epic",
                    kills: new List<ObjectiveEntry> { Obj("Draugr_Elite", 5) },
                    gathers: new List<ObjectiveEntry> { Obj("Obsidian", 10), Obj("SilverOre", 5) },
                    rewards: new List<RewardEntry> { Item("Coins", 120), Item("ArrowFrost", 40) },
                    skills: new List<SkillRewardEntry> { Skill("Blocking", 12f) },
                    preReqID: "example_mountain_wolf"));

                // Plains
                q.Add(MakeQuest(
                    "example_plains_goblin", "War in the Plains",
                    "The fulings muster for war. Break their ranks and take black metal.",
                    "Epic",
                    kills: new List<ObjectiveEntry> { Obj("Goblin", 15), Obj("GoblinBrute", 3) },
                    gathers: new List<ObjectiveEntry> { Obj("BlackMetalScrap", 8) },
                    rewards: new List<RewardEntry> { Item("Coins", 150), Item("ArrowNeedle", 40) },
                    skills: new List<SkillRewardEntry> { Skill("Polearms", 12f) },
                    expReward: 600));

                q.Add(MakeQuest(
                    "example_plains_lox", "Taming the Herd",
                    "The lox roam the grasslands. Bring a few down and carry back the meat.",
                    "Epic",
                    kills: new List<ObjectiveEntry> { Obj("Lox", 3) },
                    gathers: new List<ObjectiveEntry> { Obj("LoxMeat", 5) },
                    rewards: new List<RewardEntry> { Item("Coins", 120), Item("MeadHealthMedium", 3) },
                    skills: new List<SkillRewardEntry> { Skill("Spears", 12f) },
                    preReqID: "example_plains_goblin"));

                // Ocean
                q.Add(MakeQuest(
                    "example_ocean_fish", "Gone Fishing",
                    "Cast a line off the shore and bring back a good haul of raw fish.",
                    "Common",
                    gathers: new List<ObjectiveEntry> { Obj("FishRaw", 10) },
                    rewards: new List<RewardEntry> { Item("Coins", 40), Item("MeadStaminaMinor", 3) },
                    skills: new List<SkillRewardEntry> { Skill("Swim", 6f) }));

                q.Add(MakeQuest(
                    "example_ocean_serpent", "Terror of the Deep",
                    "A serpent prowls the open water. Slay it and harvest its meat and scales.",
                    "Legendary",
                    kills: new List<ObjectiveEntry> { Obj("Serpent", 1) },
                    gathers: new List<ObjectiveEntry> { Obj("SerpentScale", 3), Obj("SerpentMeat", 3) },
                    rewards: new List<RewardEntry> { Item("Coins", 200), Item("MeadHealthMedium", 5), Item("ArrowSilver", 50) },
                    skills: new List<SkillRewardEntry> { Skill("Spears", 15f) },
                    expReward: 800,
                    preReqID: "example_ocean_fish"));

                // Mistlands
                q.Add(MakeQuest(
                    "example_mistlands_seeker", "The Mist Hunters",
                    "Seekers skitter through the mist. Cut them apart for meat and carapace.",
                    "Epic",
                    kills: new List<ObjectiveEntry> { Obj("Seeker", 8) },
                    gathers: new List<ObjectiveEntry> { Obj("Carapace", 6), Obj("BugMeat", 4) },
                    rewards: new List<RewardEntry> { Item("Coins", 150), Item("ArrowNeedle", 40) },
                    skills: new List<SkillRewardEntry> { Skill("Knives", 12f) },
                    expReward: 700));

                q.Add(MakeQuest(
                    "example_mistlands_brute", "Hive Breaker",
                    "Break the hive's guardians: a brute and a gjall, and take the royal jelly.",
                    "Legendary",
                    kills: new List<ObjectiveEntry> { Obj("SeekerBrute", 3), Obj("Gjall", 1) },
                    gathers: new List<ObjectiveEntry> { Obj("RoyalJelly", 3) },
                    rewards: new List<RewardEntry> { Item("Coins", 200), Item("MeadHealthMedium", 4) },
                    skills: new List<SkillRewardEntry> { Skill("Blocking", 14f) },
                    expReward: 900,
                    preReqID: "example_mistlands_seeker"));

                // AshLands
                q.Add(MakeQuest(
                    "example_ashlands_charred", "Ashes to Ashes",
                    "The charred legions march. Cut down warriors and archers and gather their bones.",
                    "Legendary",
                    kills: new List<ObjectiveEntry> { Obj("Charred_Melee", 8), Obj("Charred_Archer", 4) },
                    gathers: new List<ObjectiveEntry> { Obj("CharredBone", 8) },
                    rewards: new List<RewardEntry> { Item("Coins", 200), Item("ArrowFrost", 40) },
                    skills: new List<SkillRewardEntry> { Skill("Swords", 14f) },
                    expReward: 1000));

                q.Add(MakeQuest(
                    "example_ashlands_morgen", "Bone and Bile",
                    "Hunt the morgen and a bonemaw serpent, and cut out a morgen's heart.",
                    "Legendary",
                    kills: new List<ObjectiveEntry> { Obj("Morgen", 3), Obj("BonemawSerpent", 1) },
                    gathers: new List<ObjectiveEntry> { Obj("MorgenHeart", 3) },
                    rewards: new List<RewardEntry> { Item("Coins", 300), Item("MeadHealthMedium", 5) },
                    skills: new List<SkillRewardEntry> { Skill("Polearms", 15f) },
                    expReward: 1200,
                    preReqID: "example_ashlands_charred"));

                // Deep North
                q.Add(MakeQuest(
                    "example_deepnorth_moose", "The Great Hunt",
                    "Moose roam the frozen wastes. Bring them down for hide and meat.",
                    "Legendary",
                    kills: new List<ObjectiveEntry> { Obj("Moose", 6) },
                    gathers: new List<ObjectiveEntry> { Obj("MooseHide", 5), Obj("MooseMeat", 5) },
                    rewards: new List<RewardEntry> { Item("Coins", 250), Item("ArrowSilver", 50) },
                    skills: new List<SkillRewardEntry> { Skill("Bows", 14f) },
                    expReward: 1000));

                q.Add(MakeQuest(
                    "example_deepnorth_frozen", "The Frozen Heart",
                    "Face the barka and the fryslings of the deep cold, and claim their frost cores.",
                    "Legendary",
                    kills: new List<ObjectiveEntry> { Obj("Frysling", 6), Obj("Barka", 2) },
                    gathers: new List<ObjectiveEntry> { Obj("FrostCore", 4), Obj("BarkaBranch", 2) },
                    rewards: new List<RewardEntry> { Item("Coins", 400), Item("MeadHealthMedium", 6), Item("ArrowFrost", 50) },
                    skills: new List<SkillRewardEntry> { Skill("Spears", 16f) },
                    expReward: 1500,
                    preReqID: "example_deepnorth_moose"));

                File.WriteAllText(path, JsonConvert.SerializeObject(starterPack, Formatting.Indented));
                logger.LogMessage("Created starter quest file at " + path + " with " + q.Count + " quest(s).");
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Exception caught while creating the default quest file: {ex}");
            }
        }
    }
}