using System.Collections.Generic;

namespace RtDQuestForge
{
    public class QuestList
    {
        public List<QuestConfig> Quests = new List<QuestConfig>();
    }

    public class QuestConfig
    {
        public string ID;

        public string Title;

        public string Goal;

        public string Rarity = "Common";

        // Optional, if set, this quest only becomes available once the
        // referenced quest ID is in the player's completed list.
        public string PreReqID;

        public List<ObjectiveEntry> KillReqs = new List<ObjectiveEntry>();

        public List<ObjectiveEntry> GatherReqs = new List<ObjectiveEntry>();

        public List<RewardEntry> RewardItems = new List<RewardEntry>();

        public List<SkillRewardEntry> SkillRewards = new List<SkillRewardEntry>();

        // Only applied if EpicMMOSystem is detected as an installed plugin.
        // Ignored otherwise.
        public int ExpReward;

        // Set at load time from the file the quest came from, used by the
        // journal to page between quest packs. Not part of the JSON schema.
        public string SourceFile;
    }

    public class ObjectiveEntry
    {
        public string Prefab;

        public int Amount;
    }

    public class RewardEntry
    {
        public string Prefab;

        public int Amount = 1;
    }

    public class SkillRewardEntry
    {
        // Must match a Skills.SkillType like "Swords", "Bows", "Sneak".
        public string Skill;

        public float Amount;
    }

    public class QuestProgressData
    {
        public HashSet<string> CompletedQuestIDs = new HashSet<string>();

        // Quests the player has explicitly accepted. Only accepted quests
        // gain kill and gather progress.
        public HashSet<string> AcceptedQuestIDs = new HashSet<string>();

        public HashSet<string> TrackedQuestIDs = new HashSet<string>();

        // Keyed as "{questID}:{prefabName}" so the same monster or item can be
        // tracked independently across multiple active quests.
        public Dictionary<string, int> KillCounts = new Dictionary<string, int>();

        public Dictionary<string, int> GatherCounts = new Dictionary<string, int>();
    }
}