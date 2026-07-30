using HarmonyLib;

namespace RtDQuestForge.Patches
{
    // Fires exactly once when any Character dies, on whichever machine owns
    // the creature. m_lastHit holds the HitData that killed it. If the killer
    // is the local player the kill registers directly; if the killer is a
    // remote player the credit is routed to them over the kill credit RPC,
    // because their own machine never sees this OnDeath at all.
    [HarmonyPatch(typeof(Character), "OnDeath")]
    internal static class Patch_Character_OnDeath_KillTracking
    {
        private static readonly System.Reflection.FieldInfo LastHitField = AccessTools.Field(typeof(Character), "m_lastHit");

        private static void Postfix(Character __instance)
        {
            if (__instance == null) return;
            if (__instance is Player) return;

            HitData lastHit = LastHitField != null ? (HitData)LastHitField.GetValue(__instance) : null;
            if (lastHit == null) return;

            Character attacker = lastHit.GetAttacker();
            Player attackerPlayer = attacker as Player;
            if (attackerPlayer == null) return;

            string prefabName = Utils.GetPrefabName(__instance.gameObject);

            if (Player.m_localPlayer != null && attackerPlayer == Player.m_localPlayer)
            {
                // Our own kill on our own machine, no network needed.
                if (QuestForgePlugin.Manager != null)
                {
                    QuestForgePlugin.Manager.RegisterKill(prefabName);
                }
            }
            else
            {
                // A remote player's kill died on our machine (or on the
                // dedicated server). Route the credit to the killer.
                // DEBUG breadcrumb, remove once kill routing is verified.
                Jotunn.Logger.LogMessage("Routing kill credit: " + attackerPlayer.GetPlayerName() + " killed " + prefabName);

                QuestSync.SendKillCredit(attackerPlayer.GetPlayerName(), prefabName);
            }
        }
    }

    // Fires whenever the local player successfully picks up an item, used for
    // gather-type objectives (wood, stone, ore, crops, etc).
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData))]
    internal static class Patch_Inventory_AddItem_GatherTracking
    {
        private static void Postfix(Inventory __instance, ItemDrop.ItemData item, bool __result)
        {
            if (!__result || item == null || item.m_dropPrefab == null || Player.m_localPlayer == null) return;
            if (__instance != Player.m_localPlayer.GetInventory()) return;

            if (QuestForgePlugin.Manager != null)
            {
                QuestForgePlugin.Manager.RegisterGather(item.m_dropPrefab.name, item.m_stack);
            }
        }
    }

    // Loads per-character progress once a character spawns into the world,
    // requests the authoritative quest list from the server, and wires the
    // completion event to reward granting + UI refresh.
    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class Patch_Player_OnSpawned_LoadProgress
    {
        private static bool EventsWired;

        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer || QuestForgePlugin.Manager == null) return;

            QuestForgePlugin.Manager.LoadProgress(__instance.GetPlayerName());

            // On dedicated servers this pulls the server's quest list so all
            // clients see the same quests. No-ops in singleplayer and for hosts.
            QuestSync.RequestFromServer();

            if (EventsWired) return;

            EventsWired = true;
            QuestForgePlugin.Manager.OnQuestCompleted += QuestForgePlugin.HandleQuestCompleted;
        }
    }
}