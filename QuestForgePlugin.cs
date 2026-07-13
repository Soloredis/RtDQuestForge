using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Jotunn.Managers;
using Jotunn.Utils;

namespace RtDQuestForge
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    [BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.ValheimModding.NewtonsoftJsonDetector", BepInDependency.DependencyFlags.HardDependency)]
    internal partial class QuestForgePlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "soloredis.rtdquestforge";

        public const string PluginName = "RtDQuestForge";

        public const string PluginVersion = "0.1.4";

        public static QuestManager Manager;

        private static BepInEx.Logging.ManualLogSource StaticLogger;

        private static ConfigEntry<bool> LoggingEnable;

        private ConfigEntry<KeyboardShortcut> JournalHotkey;

        private Harmony HarmonyInstance;

        private UI.QuestJournalPanel Journal;

        // Verbose diagnostic logging (kill credit routing breadcrumbs and the
        // like), gated behind the Logging.Enable config value.
        public static bool VerboseLogging
        {
            get { return LoggingEnable != null && LoggingEnable.Value; }
        }

        private void Awake()
        {
            StaticLogger = Logger;

            CreateConfigs();
            JSONSupport();
            LoadQuests();
            PatchGame();
            QuestSync.Init(Logger);

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void CreateConfigs()
        {
            try
            {
                Config.SaveOnConfigSet = true;

                LoggingEnable = Config.Bind("Logging", "Enable", false, new ConfigDescription("Enables verbose diagnostic logging.", null, new ConfigurationManagerAttributes
                {
                    IsAdminOnly = false
                }));

                JournalHotkey = Config.Bind("General", "JournalHotkey", new KeyboardShortcut(KeyCode.L), new ConfigDescription("Key to open the quest journal.", null, new ConfigurationManagerAttributes
                {
                    IsAdminOnly = false
                }));
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while adding configuration values: {ex}");
            }
        }

        // Loads every json in config/RtDQuestForge/translations as a language
        // file. File name is the language name, e.g. English.json, German.json.
        // A default English.json is written on first run so users and server
        // admins have a template to copy for other languages.
        private void JSONSupport()
        {
            try
            {
                string translationsDir = Path.Combine(BepInEx.Paths.ConfigPath, PluginName, "translations");
                Directory.CreateDirectory(translationsDir);

                string englishPath = Path.Combine(translationsDir, "English.json");
                if (!File.Exists(englishPath))
                {
                    File.WriteAllText(englishPath,
                        "{\n" +
                        "  \"rtdqf_journal_title\": \"Quest Journal\",\n" +
                        "  \"rtdqf_active\": \"Active\",\n" +
                        "  \"rtdqf_completed\": \"Completed\",\n" +
                        "  \"rtdqf_page\": \"Page\",\n" +
                        "  \"rtdqf_accept\": \"Accept\",\n" +
                        "  \"rtdqf_abandon\": \"Abandon\",\n" +
                        "  \"rtdqf_slay\": \"Slay\",\n" +
                        "  \"rtdqf_gather\": \"Gather\",\n" +
                        "  \"rtdqf_reward\": \"Reward\",\n" +
                        "  \"rtdqf_requires\": \"Requires\",\n" +
                        "  \"rtdqf_mmo_exp\": \"MMO EXP\",\n" +
                        "  \"rtdqf_quest_complete\": \"Quest Complete\"\n" +
                        "}\n");
                    Logger.LogMessage("Created default translation file at " + englishPath);
                }

                Jotunn.Entities.CustomLocalization localization = LocalizationManager.Instance.GetLocalization();

                foreach (string file in Directory.GetFiles(translationsDir, "*.json"))
                {
                    string lang = Path.GetFileNameWithoutExtension(file);
                    localization.AddJsonFile(lang, File.ReadAllText(file));
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while loading localization files: {ex}");
            }
        }

        private void LoadQuests()
        {
            try
            {
                string configDir = Path.Combine(BepInEx.Paths.ConfigPath, PluginName);
                Manager = new QuestManager(configDir, Logger);
                Manager.LoadQuestDefinitions();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while loading quest definitions: {ex}");
            }
        }

        private void PatchGame()
        {
            try
            {
                HarmonyInstance = new Harmony(PluginGUID);
                HarmonyInstance.PatchAll();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while applying Harmony patches: {ex}");
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "main") return;

            BuildUI();
        }

        private void BuildUI()
        {
            try
            {
                if (Journal == null)
                {
                    GameObject host = new GameObject("QuestForge_JournalHost");
                    DontDestroyOnLoad(host);
                    Journal = host.AddComponent<UI.QuestJournalPanel>();
                    Journal.Init(Manager, Logger);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while building UI: {ex}");
            }
        }

        private void Update()
        {
            if (Player.m_localPlayer == null || Journal == null) return;

            // While the journal is open, closing takes priority over everything.
            // Esc is read directly since BlockInput suppresses the vanilla menu.
            if (Journal.IsOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Menu.IsVisible() || JournalHotkey.Value.IsDown())
                {
                    Journal.Hide();
                }
                return;
            }

            // Never OPEN via the hotkey while the player is typing into chat,
            // the console, or a text input like signs and portal tags.
            if (Chat.instance != null && Chat.instance.HasFocus()) return;
            if (Console.IsVisible()) return;
            if (TextInput.IsVisible()) return;

            if (JournalHotkey.Value.IsDown())
            {
                Journal.Toggle();
            }
        }

        // Wired from Patches/TrackingPatches.cs when QuestManager.OnQuestCompleted fires.
        public static void HandleQuestCompleted(QuestConfig quest)
        {
            try
            {
                RewardGranter.GrantRewards(quest, StaticLogger);

                if (Player.m_localPlayer != null)
                {
                    string completeText = Localization.instance != null
                        ? Localization.instance.Localize("$rtdqf_quest_complete")
                        : "Quest Complete";

                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, completeText + ": " + quest.Title);
                }
            }
            catch (Exception ex)
            {
                StaticLogger?.LogWarning($"Exception caught while granting quest rewards: {ex}");
            }
        }
    }
}