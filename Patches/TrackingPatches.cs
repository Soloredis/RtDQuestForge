using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RtDQuestForge.Patches
{
    // Fires exactly once when any Character dies, on whichever machine owns
    // the creature. m_lastHit holds the HitData that killed it. The killer
    // and any SocialSystem party members near the kill all receive credit.
    // Each recipient on this machine registers directly; remote recipients
    // get it over the kill credit RPC, since their own machine never sees
    // this OnDeath at all.
    [HarmonyPatch(typeof(Character), "OnDeath")]
    internal static class Patch_Character_OnDeath_KillTracking
    {
        private static readonly System.Reflection.FieldInfo LastHitField = AccessTools.Field(typeof(Character), "m_lastHit");

        // SocialSystem stores each player's party ID in their ZDO under this
        // key. ZDO data is synced to every machine, so it can be read here
        // without referencing SocialSystem at all. 0 means no party, which is
        // also what every player reads when SocialSystem is not installed.
        private static readonly int PartyIdHash = "SocialSystem_PartyId".GetStableHashCode();

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

            foreach (Player recipient in GetRecipients(attackerPlayer, __instance.transform.position))
            {
                if (Player.m_localPlayer != null && recipient == Player.m_localPlayer)
                {
                    // Our own credit on our own machine, no network needed.
                    if (QuestForgePlugin.Manager != null)
                    {
                        QuestForgePlugin.Manager.RegisterKill(prefabName);
                    }
                }
                else
                {
                    // A remote player's credit. Route it to them.
                    if (QuestForgePlugin.VerboseLogging)
                    {
                        Jotunn.Logger.LogMessage("Routing kill credit: " + recipient.GetPlayerName() + " for " + prefabName);
                    }

                    QuestSync.SendKillCredit(recipient.GetPlayerID(), prefabName);
                }
            }
        }

        // The killer, plus every living party member within range of the kill.
        private static List<Player> GetRecipients(Player killer, Vector3 killPosition)
        {
            List<Player> recipients = new List<Player>();
            recipients.Add(killer);

            if (!QuestForgePlugin.PartySharing) return recipients;

            int killerParty = GetPartyId(killer);
            if (killerParty == 0) return recipients;

            float range = QuestForgePlugin.PartyShareRange;

            foreach (Player player in Player.GetAllPlayers())
            {
                if (player == null || player == killer || player.IsDead()) continue;
                if (GetPartyId(player) != killerParty) continue;
                if (Vector3.Distance(player.transform.position, killPosition) > range) continue;

                recipients.Add(player);
            }

            return recipients;
        }

        private static int GetPartyId(Player player)
        {
            ZNetView nview = player != null ? player.GetComponent<ZNetView>() : null;
            ZDO zdo = nview != null ? nview.GetZDO() : null;
            return zdo != null ? zdo.GetInt(PartyIdHash, 0) : 0;
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