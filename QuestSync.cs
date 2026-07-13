using System;
using System.Collections;
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

        private static ManualLogSource Logger;

        public static void Init(ManualLogSource logger)
        {
            try
            {
                Logger = logger;
                SyncRpc = NetworkManager.Instance.AddRPC("rtdqf_questsync", OnServerReceive, OnClientReceive);
                KillRpc = NetworkManager.Instance.AddRPC("rtdqf_killcredit", OnKillServerReceive, OnKillClientReceive);
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
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while requesting quest sync: {ex}");
            }
        }

        // Called from the OnDeath patch when the killing blow came from a
        // player who is not the local player on this machine. The package
        // goes to the server, which forwards it to every client. Killer only:
        // exactly one player name is carried, only that player gains progress.
        public static void SendKillCredit(string killerPlayerName, string prefabName)
        {
            try
            {
                if (KillRpc == null) return;

                // DEBUG handshake
                if (QuestForgePlugin.VerboseLogging)
                {
                    Logger.LogMessage("Broadcasting kill credit package for " + killerPlayerName + " (" + prefabName + ").");
                }

                ZPackage package = new ZPackage();
                package.Write(killerPlayerName);
                package.Write(prefabName);

                KillRpc.SendPackage(ZRoutedRpc.Everybody, package);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while sending kill credit for {prefabName}: {ex}");
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

        // Kill credit arriving at the dedicated server. The server forwards
        // the package to every connected client itself, rather than trusting
        // broadcast semantics: the named killer's own client registers it.
        private static IEnumerator OnKillServerReceive(long sender, ZPackage package)
        {
            try
            {
                string killerPlayerName = package.ReadString();
                string prefabName = package.ReadString();

                // DEBUG handshake
                if (QuestForgePlugin.VerboseLogging)
                {
                    Logger.LogMessage("Server forwarding kill credit for " + killerPlayerName + " (" + prefabName + ") to all peers.");
                }

                foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                {
                    ZPackage forward = new ZPackage();
                    forward.Write(killerPlayerName);
                    forward.Write(prefabName);

                    KillRpc.SendPackage(peer.m_uid, forward);
                }
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

                string killerPlayerName = package.ReadString();
                string prefabName = package.ReadString();

                if (Player.m_localPlayer != null
                    && Player.m_localPlayer.GetPlayerName() == killerPlayerName
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