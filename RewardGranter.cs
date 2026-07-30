using System;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using UnityEngine;

namespace RtDQuestForge
{
    public static class RewardGranter
    {
        // Soft dependency, only touched via reflection and only if the plugin
        // GUID below is actually loaded. Works fine with or without EpicMMOSystem.
        private const string EpicMmoGuid = "WackyMole.EpicMMOSystem";

        public static void GrantRewards(QuestConfig quest, ManualLogSource logger)
        {
            if (Player.m_localPlayer == null) return;

            GrantItems(quest, logger);
            GrantSkillXP(quest, logger);
            GrantEpicMmoXP(quest, logger);
        }

        private static void GrantItems(QuestConfig quest, ManualLogSource logger)
        {
            try
            {
                foreach (RewardEntry reward in quest.RewardItems)
                {
                    GameObject prefab = ZNetScene.instance.GetPrefab(reward.Prefab);
                    if (prefab == null)
                    {
                        logger.LogWarning("Reward prefab '" + reward.Prefab + "' on quest '" + quest.ID + "' does not exist and was skipped.");
                        continue;
                    }

                    ItemDrop itemDrop = prefab.GetComponent<ItemDrop>();
                    if (itemDrop == null)
                    {
                        logger.LogWarning("Prefab '" + reward.Prefab + "' on quest '" + quest.ID + "' has no ItemDrop component.");
                        continue;
                    }

                    ItemDrop.ItemData itemData = itemDrop.m_itemData.Clone();
                    itemData.m_stack = reward.Amount;

                    Inventory inventory = Player.m_localPlayer.GetInventory();
                    if (!inventory.AddItem(itemData))
                    {
                        Player.m_localPlayer.DropItem(inventory, itemData, itemData.m_stack);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Exception caught while granting item rewards for quest '{quest.ID}': {ex}");
            }
        }

        private static void GrantSkillXP(QuestConfig quest, ManualLogSource logger)
        {
            try
            {
                foreach (SkillRewardEntry skillReward in quest.SkillRewards)
                {
                    Skills.SkillType skillType;
                    if (!Enum.TryParse(skillReward.Skill, true, out skillType))
                    {
                        logger.LogWarning("Unknown skill '" + skillReward.Skill + "' on quest '" + quest.ID + "'.");
                        continue;
                    }
                    Player.m_localPlayer.RaiseSkill(skillType, skillReward.Amount);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Exception caught while granting skill XP for quest '{quest.ID}': {ex}");
            }
        }

        private static void GrantEpicMmoXP(QuestConfig quest, ManualLogSource logger)
        {
            if (quest.ExpReward <= 0 || !Chainloader.PluginInfos.ContainsKey(EpicMmoGuid)) return;

            try
            {
                object epicMmoInstance = Chainloader.PluginInfos[EpicMmoGuid].Instance;
                Type levelSystemType = epicMmoInstance.GetType().Assembly.GetType("EpicMMOSystem.LevelSystem");
                if (levelSystemType == null) return;

                // LevelSystem is a singleton, grab the static Instance property first dipshit
                PropertyInfo instanceProperty = levelSystemType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
                object levelSystem = instanceProperty != null ? instanceProperty.GetValue(null) : null;
                if (levelSystem == null) return;

                // AddExp is an instance method, and its signature differs between
                // EpicMMO versions, don't assume shit.
                MethodInfo addExpMethod = levelSystemType.GetMethod("AddExp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (addExpMethod == null) return;

                ParameterInfo[] parameters = addExpMethod.GetParameters();

                if (parameters.Length == 1)
                {
                    object expArg = Convert.ChangeType(quest.ExpReward, parameters[0].ParameterType);
                    addExpMethod.Invoke(levelSystem, new object[1] { expArg });
                }
                else if (parameters.Length == 2)
                {
                    object arg1 = parameters[0].ParameterType == typeof(Player) ? (object)Player.m_localPlayer : Convert.ChangeType(quest.ExpReward, parameters[0].ParameterType);
                    object arg2 = parameters[1].ParameterType == typeof(Player) ? (object)Player.m_localPlayer : Convert.ChangeType(quest.ExpReward, parameters[1].ParameterType);
                    addExpMethod.Invoke(levelSystem, new object[2] { arg1, arg2 });
                }
            }
            catch (Exception ex)
            {
                // EpicMMO's internal API can differ between versions.... never let this take down quest completion.
                logger.LogWarning($"Exception caught while granting EpicMMO XP for quest '{quest.ID}': {ex}");
            }
        }
    }
}
