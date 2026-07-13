using UnityEngine;

namespace RtDQuestForge.UI
{
    internal static class QuestUIStyles
    {
        public static Color RarityColor(string rarity)
        {
            string rarityLower = rarity != null ? rarity.ToLowerInvariant() : "";

            if (rarityLower == "common") return new Color(0.75f, 0.75f, 0.75f);
            if (rarityLower == "uncommon") return new Color(0.30f, 0.80f, 0.30f);
            if (rarityLower == "rare") return new Color(0.30f, 0.55f, 1.00f);
            if (rarityLower == "epic") return new Color(0.65f, 0.30f, 0.90f);
            if (rarityLower == "legendary") return new Color(1.00f, 0.65f, 0.10f);

            return Color.white;
        }
    }
}
