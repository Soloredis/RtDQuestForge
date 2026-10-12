using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using Jotunn.Entities;
using Jotunn.Managers;
using Newtonsoft.Json;

namespace RtDQuestForge
{
    // Server to client quest list synchronization plus kill credit routing.
    // The server is the single source of truth for quests: clients request
    // the quest list when their player spawns. Kill credit solves creature
    // ownership: deaths are detected on whichever machine owns the creature,
    // then credit is routed to the player who landed the killing blow.
    public static class QuestSync
    {
        private static CustomRPC SyncRpc;

        private static CustomRPC KillRpc;

        private static CustomRPC CompletionRpc;

        // Server side record of completed quests, keyed by player ID. Saved
        // to config/RtDQuestForge/serverdata/completions.json. The subfolder
        // keeps it out of the quest loader, which only scans the top folder.
        private static Dictionary<long, HashSet<string>> ServerCompletions;

        private const byte CompletionSync = 0;

        private const byte CompletionRecord = 1;

        private static ManualLogSource Logger;

        public static void Init(ManualLogSource logger)
        {
            try
            {
                Logger = logger;
                SyncRpc = NetworkManager.Instance.AddRPC("rtdqf_questsync", OnServerReceive, OnClientReceive);
                KillRpc = NetworkManager.Instance.AddRPC("rtdqf_killcredit", OnKillServerReceive, OnKillClientReceive);
                CompletionRpc = NetworkManager.Instance.AddRPC("rtdqf_completions", OnCompletionServerReceive, OnCompletionClientReceive);
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Exception caught while registering the quest sync RPCs: {ex}");
            }
        }

        // Called on the client after the local player spawns. Local hosts and
        // singleplayer are the server themselves, so they skip the request.
        public static void RequestFromServer()
        {
            try
            {
                if (ZNet.instance == null || ZNet.instance.IsServer()) return;
                if (SyncRpc == null) return;

                ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
                if (serverPeer == null) return;

                SyncRpc.SendPackage(serverPeer.m_uid, new ZPackage());
                Logger.LogMessage("Requested quest list from the server.");

                SendCompletionSync(serverPeer);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while requesting quest sync: {ex}");
            }
        }

        // Called from the OnDeath patch when the killing blow came from a
        // player who is not the local player on this machine. Clients send the
        // package to the server; if this machine IS the server (dedicated or
        // listen host) it delivers directly. Killer only: exactly one player
        // ID is carried, only that player gains progress.
        public static void SendKillCredit(long killerPlayerID, string prefabName)
        {
            try
            {
                if (KillRpc == null || ZNet.instance == null) return;

                // DEBUG handshake
                if (QuestForgePlugin.VerboseLogging)
                {
                    Logger.LogMessage("Broadcasting kill credit package for " + killerPlayerID + " (" + prefabName + ").");
                }

                if (ZNet.instance.IsServer())
                {
                    DeliverKillCredit(killerPlayerID, prefabName);
                    return;
                }

                ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
                if (serverPeer == null) return;

                ZPackage package = new ZPackage();
                package.Write(killerPlayerID);
                package.Write(prefabName);

                KillRpc.SendPackage(serverPeer.m_uid, package);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while sending kill credit for {prefabName}: {ex}");
            }
        }

        // Server side delivery. A listen host's own player is not a peer, so
        // it is checked locally first; every remote peer then gets a copy and
        // only the matching player registers it.
        private static void DeliverKillCredit(long killerPlayerID, string prefabName)
        {
            if (Player.m_localPlayer != null
                && Player.m_localPlayer.GetPlayerID() == killerPlayerID
                && QuestForgePlugin.Manager != null)
            {
                QuestForgePlugin.Manager.RegisterKill(prefabName);
            }

            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                ZPackage forward = new ZPackage();
                forward.Write(killerPlayerID);
                forward.Write(prefabName);

                KillRpc.SendPackage(peer.m_uid, forward);
            }
        }

        // Sends this character's local completions to the server on spawn.
        // The server merges them into its record and answers with the full
        // list, so wiping local configs cannot reset completed quests, and
        // completions from before this feature carry over.
        private static void SendCompletionSync(ZNetPeer serverPeer)
        {
            if (CompletionRpc == null || Player.m_localPlayer == null || QuestForgePlugin.Manager == null) return;

            ZPackage package = new ZPackage();
            package.Write(CompletionSync);
            package.Write(Player.m_localPlayer.GetPlayerID());

            HashSet<string> local = QuestForgePlugin.Manager.Progress.CompletedQuestIDs;
            package.Write(local.Count);
            foreach (string questID in local)
            {
                package.Write(questID);
            }

            CompletionRpc.SendPackage(serverPeer.m_uid, package);
        }

        // Called when the local player completes a quest. Hosts and
        // singleplayer keep their own progress file as the record.
        public static void ReportCompletion(string questID)
        {
            try
            {
                if (CompletionRpc == null || ZNet.instance == null || ZNet.instance.IsServer()) return;
                if (Player.m_localPlayer == null) return;

                ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
                if (serverPeer == null) return;

                ZPackage package = new ZPackage();
                package.Write(CompletionRecord);
                package.Write(Player.m_localPlayer.GetPlayerID());
                package.Write(questID);

                CompletionRpc.SendPackage(serverPeer.m_uid, package);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while reporting quest completion {questID}: {ex}");
            }
        }

        // Runs on the server. A sync merges and answers with the full list;
        // a record just adds the one quest.
        private static IEnumerator OnCompletionServerReceive(long sender, ZPackage package)
        {
            try
            {
                byte kind = package.ReadByte();
                long playerID = package.ReadLong();
                HashSet<string> completed = GetServerCompletions(playerID);
                bool changed = false;

                if (kind == CompletionSync)
                {
                    int count = package.ReadInt();
                    for (int i = 0; i < count; i++)
                    {
                        if (completed.Add(package.ReadString())) changed = true;
                    }

                    ZPackage response = new ZPackage();
                    response.Write(completed.Count);
                    foreach (string questID in completed)
                    {
                        response.Write(questID);
                    }

                    CompletionRpc.SendPackage(sender, response);
                }
                else if (kind == CompletionRecord)
                {
                    if (completed.Add(package.ReadString())) changed = true;
                }

                if (changed)
                {
                    SaveServerCompletions();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while handling quest completions from peer {sender}: {ex}");
            }

            yield break;
        }

        // Runs on the client when the server's completion list arrives.
        private static IEnumerator OnCompletionClientReceive(long sender, ZPackage package)
        {
            try
            {
                int count = package.ReadInt();
                List<string> completed = new List<string>(count);
                for (int i = 0; i < count; i++)
                {
                    completed.Add(package.ReadString());
                }

                if (QuestForgePlugin.Manager != null)
                {
                    QuestForgePlugin.Manager.ApplyServerCompletions(completed);
                    Logger.LogMessage("Applied " + completed.Count + " completed quest(s) from the server.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while applying completed quests from the server: {ex}");
            }

            yield break;
        }

        private static string ServerCompletionsPath()
        {
            return Path.Combine(BepInEx.Paths.ConfigPath, QuestForgePlugin.PluginName, "serverdata", "completions.json");
        }

        private static HashSet<string> GetServerCompletions(long playerID)
        {
            if (ServerCompletions == null)
            {
                ServerCompletions = new Dictionary<long, HashSet<string>>();

                string path = ServerCompletionsPath();
                if (File.Exists(path))
                {
                    try
                    {
                        Dictionary<long, HashSet<string>> loaded = JsonConvert.DeserializeObject<Dictionary<long, HashSet<string>>>(File.ReadAllText(path));
                        if (loaded != null) ServerCompletions = loaded;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Exception caught while loading server completions, starting empty: {ex}");
                    }
                }
            }

            HashSet<string> completed;
            if (!ServerCompletions.TryGetValue(playerID, out completed))
            {
                completed = new HashSet<string>();
                ServerCompletions[playerID] = completed;
            }

            return completed;
        }

        private static void SaveServerCompletions()
        {
            try
            {
                string path = ServerCompletionsPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(ServerCompletions, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while saving server completions: {ex}");
            }
        }

        // Runs on the server when a client asks for the quest list.
        private static IEnumerator OnServerReceive(long sender, ZPackage package)
        {
            try
            {
                string json = JsonConvert.SerializeObject(QuestForgePlugin.Manager.AllQuests);

                ZPackage response = new ZPackage();
                response.Write(json);

                SyncRpc.SendPackage(sender, response);
                Logger.LogMessage("Sent quest list to peer " + sender + ".");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while sending quest sync to peer {sender}: {ex}");
            }

            yield break;
        }

        // Runs on the client when the server's quest list arrives.
        private static IEnumerator OnClientReceive(long sender, ZPackage package)
        {
            try
            {
                string json = package.ReadString();
                QuestList serverList = JsonConvert.DeserializeObject<QuestList>(json);

                if (serverList != null && serverList.Quests != null)
                {
                    QuestForgePlugin.Manager.ApplyServerQuestList(serverList);
                    Logger.LogMessage("Applied " + serverList.Quests.Count + " quest(s) from the server.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while applying quest sync from the server: {ex}");
            }

            yield break;
        }

        // Kill credit arriving at the server (dedicated or listen host) from
        // a client that owned the creature. Delivery is shared with the host
        // path above so the host's own player is never skipped.
        private static IEnumerator OnKillServerReceive(long sender, ZPackage package)
        {
            try
            {
                long killerPlayerID = package.ReadLong();
                string prefabName = package.ReadString();

                // DEBUG handshake
                if (QuestForgePlugin.VerboseLogging)
                {
                    Logger.LogMessage("Server forwarding kill credit for " + killerPlayerID + " (" + prefabName + ") to all peers.");
                }

                DeliverKillCredit(killerPlayerID, prefabName);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while forwarding kill credit: {ex}");
            }

            yield break;
        }

        // Kill credit arriving at a client. Only the named killer registers it.
        private static IEnumerator OnKillClientReceive(long sender, ZPackage package)
        {
            try
            {
                // DEBUG handshake
                if (QuestForgePlugin.VerboseLogging)
                {
                    Logger.LogMessage("Kill credit package received from peer " + sender + ".");
                }

                long killerPlayerID = package.ReadLong();
                string prefabName = package.ReadString();

                if (Player.m_localPlayer != null
                    && Player.m_localPlayer.GetPlayerID() == killerPlayerID
                    && QuestForgePlugin.Manager != null)
                {
                    if (QuestForgePlugin.VerboseLogging)
                    {
                        Logger.LogMessage("Kill credit matched local player, registering " + prefabName + ".");
                    }

                    QuestForgePlugin.Manager.RegisterKill(prefabName);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while applying kill credit: {ex}");
            }

            yield break;
        }
    }
}