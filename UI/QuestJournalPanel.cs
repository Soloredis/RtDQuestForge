using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace RtDQuestForge.UI
{
    public class QuestJournalPanel : MonoBehaviour
    {
        private QuestManager Manager;

        private ManualLogSource Logger;

        private GameObject RootPanel;

        private RectTransform ActiveListContainer;

        private RectTransform CompletedListContainer;

        private readonly List<GameObject> EntryPool = new List<GameObject>();
        
        private int CurrentPageIndex;
        
        private float lastPageChange;

        private List<string> SourceFiles = new List<string>();

        private GameObject PagerRoot;

        private Text PagerLabel;

        public bool IsOpen
        {
            get { return RootPanel != null && RootPanel.activeSelf; }
        }
        
        private static string L(string token)
        {
            return Localization.instance != null ? Localization.instance.Localize(token) : token;
        }

        public void Init(QuestManager manager, ManualLogSource logger)
        {
            Manager = manager;
            Logger = logger;

            BuildUI();
            Hide();
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        public void Show()
        {
            RootPanel.SetActive(true);
            Refresh();
            
            GUIManager.BlockInput(true);
        }

        public void Hide()
        {
            RootPanel.SetActive(false);
            GUIManager.BlockInput(false);
        }

        public void Refresh()
        {
            try
            {
                foreach (GameObject entry in EntryPool)
                {
                    Destroy(entry);
                }
                EntryPool.Clear();

                if (Manager == null) return;
                
                SourceFiles = Manager.AllQuests.Quests
                    .Select(q => string.IsNullOrEmpty(q.SourceFile) ? "Quests" : q.SourceFile)
                    .Distinct()
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (SourceFiles.Count == 0)
                {
                    SourceFiles.Add("Quests");
                }

                if (CurrentPageIndex >= SourceFiles.Count)
                {
                    CurrentPageIndex = 0;
                }

                string currentFile = SourceFiles[CurrentPageIndex];

                // Pager is only shown when there is more than one quest file.
                PagerRoot.SetActive(SourceFiles.Count > 1);
                PagerLabel.text = L("$rtdqf_page") + " " + (CurrentPageIndex + 1) + "/" + SourceFiles.Count;

                List<QuestConfig> pageQuests = Manager.AllQuests.Quests
                    .Where(q => (string.IsNullOrEmpty(q.SourceFile) ? "Quests" : q.SourceFile) == currentFile)
                    .ToList();

                List<QuestConfig> availableQuests = Manager.GetAvailableQuests()
                    .Where(q => pageQuests.Contains(q))
                    .ToList();
                
                List<QuestConfig> lockedQuests = pageQuests
                    .Where(q => !Manager.Progress.CompletedQuestIDs.Contains(q.ID)
                             && !string.IsNullOrEmpty(q.PreReqID)
                             && !Manager.Progress.CompletedQuestIDs.Contains(q.PreReqID))
                    .ToList();

                List<QuestConfig> completedQuests = pageQuests
                    .Where(q => Manager.Progress.CompletedQuestIDs.Contains(q.ID))
                    .ToList();

                FillColumn(ActiveListContainer, availableQuests.Concat(lockedQuests).ToList());
                FillColumn(CompletedListContainer, completedQuests);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Exception caught while refreshing the quest journal: {ex}");
            }
        }

        private void ChangePage(int direction)
        {
            if (SourceFiles.Count <= 1) return;
            
            if (Time.unscaledTime - lastPageChange < 0.2f) return;
            lastPageChange = Time.unscaledTime;
            
            CurrentPageIndex += direction;
            
            if (CurrentPageIndex < 0) CurrentPageIndex = SourceFiles.Count - 1;
            if (CurrentPageIndex >= SourceFiles.Count) CurrentPageIndex = 0;

            Refresh();
        }

        private void FillColumn(RectTransform container, List<QuestConfig> quests)
        {
            float y = 0f;
            float entryHeight = 150f;

            foreach (QuestConfig quest in quests)
            {
                GameObject entry = BuildEntry(container, quest, y);
                EntryPool.Add(entry);
                y -= entryHeight;
            }

            container.sizeDelta = new Vector2(0, Mathf.Abs(y) + 20f);
        }

        private void BuildUI()
        {
            GameObject canvasGO = new GameObject("QuestForge_JournalCanvas");
            canvasGO.transform.SetParent(transform, false);

            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();
            
            RootPanel = new GameObject("Panel");
            RootPanel.transform.SetParent(canvasGO.transform, false);

            RectTransform panelRect = RootPanel.AddComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(920, 560);
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;

            Image bg = RootPanel.AddComponent<Image>();
            Sprite woodPanel = GUIManager.Instance != null ? GUIManager.Instance.GetSprite("woodpanel_trophys") : null;
            if (woodPanel != null)
            {
                bg.sprite = woodPanel;
                bg.type = Image.Type.Sliced;
                bg.color = Color.white;
            }
            else
            {
                bg.color = new Color(0.05f, 0.05f, 0.05f, 0.92f);
            }

            // Title
            Text title = CreateText(RootPanel.transform, L("$rtdqf_journal_title"), 26, TextAnchor.MiddleCenter);
            ApplyValheimHeaderStyle(title);
            RectTransform titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.sizeDelta = new Vector2(0, 40);
            titleRect.anchoredPosition = new Vector2(0, -18);

            // Quest file pager, top right corner: [<] Page 1/3 [>]
            BuildPager();

            // Left column: active quests. Right column: completed quests.
            ActiveListContainer = BuildColumn(L("$rtdqf_active"), 0.0f, 0.5f);
            CompletedListContainer = BuildColumn(L("$rtdqf_completed"), 0.5f, 1.0f);
        }
        
        private void BuildPager()
        {
            PagerRoot = new GameObject("Pager");
            PagerRoot.transform.SetParent(RootPanel.transform, false);

            RectTransform pagerRect = PagerRoot.AddComponent<RectTransform>();
            pagerRect.anchorMin = new Vector2(1, 1);
            pagerRect.anchorMax = new Vector2(1, 1);
            pagerRect.pivot = new Vector2(1, 1);
            pagerRect.sizeDelta = new Vector2(260, 28);
            pagerRect.anchoredPosition = new Vector2(-22, -20);

            // Previous page button
            Button prevButton = CreateButton(PagerRoot.transform, "<", delegate { ChangePage(-1); }, false);
            RectTransform prevRect = prevButton.GetComponent<RectTransform>();
            prevRect.anchorMin = new Vector2(0, 0);
            prevRect.anchorMax = new Vector2(0, 1);
            prevRect.pivot = new Vector2(0, 0.5f);
            prevRect.sizeDelta = new Vector2(28, 0);
            prevRect.anchoredPosition = Vector2.zero;

            // Next page button
            Button nextButton = CreateButton(PagerRoot.transform, ">", delegate { ChangePage(1); }, false);
            RectTransform nextRect = nextButton.GetComponent<RectTransform>();
            nextRect.anchorMin = new Vector2(1, 0);
            nextRect.anchorMax = new Vector2(1, 1);
            nextRect.pivot = new Vector2(1, 0.5f);
            nextRect.sizeDelta = new Vector2(28, 0);
            nextRect.anchoredPosition = Vector2.zero;

            // Current page label between the buttons
            PagerLabel = CreateText(PagerRoot.transform, "", 12, TextAnchor.MiddleCenter);
            RectTransform labelRect = PagerLabel.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0, 0);
            labelRect.anchorMax = new Vector2(1, 1);
            labelRect.offsetMin = new Vector2(32, 0);
            labelRect.offsetMax = new Vector2(-32, 0);
        }
        
        private RectTransform BuildColumn(string header, float anchorLeft, float anchorRight)
        {
            GameObject columnGO = new GameObject("Column_" + header);
            columnGO.transform.SetParent(RootPanel.transform, false);

            RectTransform columnRect = columnGO.AddComponent<RectTransform>();
            columnRect.anchorMin = new Vector2(anchorLeft, 0);
            columnRect.anchorMax = new Vector2(anchorRight, 1);
            columnRect.offsetMin = new Vector2(18, 22);
            columnRect.offsetMax = new Vector2(-18, -62);

            // Column header
            Text headerText = CreateText(columnGO.transform, header, 18, TextAnchor.MiddleCenter);
            ApplyValheimHeaderStyle(headerText);
            RectTransform headerRect = headerText.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0.5f, 1);
            headerRect.sizeDelta = new Vector2(0, 26);
            headerRect.anchoredPosition = Vector2.zero;

            // Scroll view
            GameObject scrollGO = new GameObject("ScrollView");
            scrollGO.transform.SetParent(columnGO.transform, false);

            ScrollRect scrollRect = scrollGO.AddComponent<ScrollRect>();
            RectTransform scrollRectTransform = scrollGO.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0, 0);
            scrollRectTransform.anchorMax = new Vector2(1, 1);
            scrollRectTransform.offsetMin = new Vector2(0, 0);
            scrollRectTransform.offsetMax = new Vector2(0, -30);
            

            // Viewport
            GameObject viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollGO.transform, false);

            RectTransform viewportRect = viewportGO.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewportGO.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            viewportGO.AddComponent<Mask>().showMaskGraphic = false;

            // Scrollable content container
            GameObject contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);

            RectTransform container = contentGO.AddComponent<RectTransform>();
            container.anchorMin = new Vector2(0, 1);
            container.anchorMax = new Vector2(1, 1);
            container.pivot = new Vector2(0.5f, 1);
            container.anchoredPosition = Vector2.zero;
            container.sizeDelta = new Vector2(0, 0);

            scrollRect.viewport = viewportRect;
            scrollRect.content = container;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            
            scrollRect.scrollSensitivity = 500f;
            scrollRect.inertia = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            return container;
        }

        private GameObject BuildEntry(RectTransform parent, QuestConfig quest, float y)
        {
            GameObject entry = new GameObject("Quest_" + quest.ID);
            entry.transform.SetParent(parent, false);

            RectTransform rect = entry.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.sizeDelta = new Vector2(0, 140);
            rect.anchoredPosition = new Vector2(0, y);

            bool completed = Manager.Progress.CompletedQuestIDs.Contains(quest.ID);
            bool accepted = Manager.Progress.AcceptedQuestIDs.Contains(quest.ID);
            bool locked = !completed
                       && !string.IsNullOrEmpty(quest.PreReqID)
                       && !Manager.Progress.CompletedQuestIDs.Contains(quest.PreReqID);
            
            entry.AddComponent<Image>().color = locked ? new Color(0.35f, 0.35f, 0.35f) : QuestUIStyles.RarityColor(quest.Rarity);

            GameObject inner = new GameObject("Inner");
            inner.transform.SetParent(entry.transform, false);

            RectTransform innerRect = inner.AddComponent<RectTransform>();
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(3, 3);
            innerRect.offsetMax = new Vector2(-3, -3);
            
            Image innerImage = inner.AddComponent<Image>();
            Sprite cardSprite = GUIManager.Instance != null ? GUIManager.Instance.GetSprite("woodpanel_trophys") : null;
            if (cardSprite != null)
            {
                innerImage.sprite = cardSprite;
                innerImage.type = Image.Type.Sliced;

                if (completed)
                {
                    innerImage.color = new Color(0.55f, 0.75f, 0.55f, 1f);
                }
                else if (locked)
                {
                    innerImage.color = new Color(0.45f, 0.45f, 0.45f, 1f);
                }
                else if (accepted)
                {
                    innerImage.color = new Color(1f, 0.92f, 0.75f, 1f);
                }
                else
                {
                    innerImage.color = Color.white;
                }
            }
            else
            {
                innerImage.color = completed ? new Color(0.08f, 0.14f, 0.08f, 0.95f) : new Color(0.10f, 0.10f, 0.10f, 0.95f);
            }

            // Quest titles and goals come from the quest json. They pass through
            // Localize too, so quest authors can use $tokens in their own packs.
            Text titleText = CreateText(inner.transform, L(quest.Title), 16, TextAnchor.UpperLeft);
            RectTransform titleRect = titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0, 1);
            titleRect.sizeDelta = new Vector2(-16, 20);
            titleRect.anchoredPosition = new Vector2(10, -8);

            Text goalText = CreateText(inner.transform, L(quest.Goal), 12, TextAnchor.UpperLeft);
            RectTransform goalRect = goalText.GetComponent<RectTransform>();
            goalRect.anchorMin = new Vector2(0, 0);
            goalRect.anchorMax = new Vector2(1, 1);
            goalRect.offsetMin = new Vector2(10, 62);
            goalRect.offsetMax = new Vector2((completed || locked) ? -10 : -92, -30); // full width when no button

            // Locked quests show what unlocks them instead of objectives.
            // Everything else shows objectives with live counts plus rewards.
            string detailContent;
            if (locked)
            {
                QuestConfig prereq = Manager.AllQuests.Quests.FirstOrDefault(q => q.ID == quest.PreReqID);
                string prereqName = prereq != null ? L(prereq.Title) : quest.PreReqID;
                detailContent = L("$rtdqf_requires") + ": " + prereqName;
            }
            else
            {
                detailContent = BuildDetailLine(quest);
            }
            
            Text detailText = CreateText(inner.transform, detailContent, 11, TextAnchor.LowerLeft);
            detailText.color = new Color(0.8f, 0.8f, 0.10f); // slightly lighter than body ink
            RectTransform detailRect = detailText.GetComponent<RectTransform>();
            detailRect.anchorMin = new Vector2(0, 0);
            detailRect.anchorMax = new Vector2(1, 0);
            detailRect.pivot = new Vector2(0.5f, 0);
            detailRect.sizeDelta = new Vector2(-20, 40);
            detailRect.anchoredPosition = new Vector2(0, 16);
            
            if (!completed && !locked)
            {
                Button questButton = CreateButton(inner.transform, accepted ? L("$rtdqf_abandon") : L("$rtdqf_accept"), delegate
                {
                    if (Manager.Progress.AcceptedQuestIDs.Contains(quest.ID))
                    {
                        Manager.AbandonQuest(quest);
                    }
                    else
                    {
                        Manager.AcceptQuest(quest);
                    }

                    Refresh();
                }, false);

                RectTransform buttonRect = questButton.GetComponent<RectTransform>();
                buttonRect.anchorMin = new Vector2(1, 1);
                buttonRect.anchorMax = new Vector2(1, 1);
                buttonRect.pivot = new Vector2(1, 1);
                buttonRect.sizeDelta = new Vector2(78, 24);
                buttonRect.anchoredPosition = new Vector2(-8, -8);
            }

            return entry;
        }

        private string BuildDetailLine(QuestConfig quest)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            foreach (ObjectiveEntry kill in quest.KillReqs)
            {
                int c;
                Manager.Progress.KillCounts.TryGetValue(quest.ID + ":" + kill.Prefab, out c);
                if (sb.Length > 0) sb.Append("   ");
                sb.Append(L("$rtdqf_slay") + " " + PrettyName(kill.Prefab) + ": " + c + "/" + kill.Amount);
            }

            foreach (ObjectiveEntry gather in quest.GatherReqs)
            {
                int c;
                Manager.Progress.GatherCounts.TryGetValue(quest.ID + ":" + gather.Prefab, out c);
                if (sb.Length > 0) sb.Append("   ");
                sb.Append(L("$rtdqf_gather") + " " + PrettyName(gather.Prefab) + ": " + c + "/" + gather.Amount);
            }

            System.Text.StringBuilder rewards = new System.Text.StringBuilder();

            foreach (RewardEntry reward in quest.RewardItems)
            {
                if (rewards.Length > 0) rewards.Append(", ");
                rewards.Append(reward.Amount + " " + PrettyName(reward.Prefab));
            }

            foreach (SkillRewardEntry skill in quest.SkillRewards)
            {
                if (rewards.Length > 0) rewards.Append(", ");
                rewards.Append("+" + skill.Amount + " " + skill.Skill + " XP");
            }

            if (quest.ExpReward > 0 && BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("WackyMole.EpicMMOSystem"))
            {
                if (rewards.Length > 0) rewards.Append(", ");
                rewards.Append(quest.ExpReward + " " + L("$rtdqf_mmo_exp"));
            }

            if (rewards.Length > 0)
            {
                if (sb.Length > 0) sb.Append("\n");
                sb.Append(L("$rtdqf_reward") + ": " + rewards);
            }

            return sb.ToString();
        }
        
        private static readonly System.Collections.Generic.Dictionary<string, string> PrettyNameCache = new System.Collections.Generic.Dictionary<string, string>();

        private static string PrettyName(string prefabName)
        {
            string cached;
            if (PrettyNameCache.TryGetValue(prefabName, out cached)) return cached;

            string result = prefabName;

            try
            {
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;

                if (prefab != null)
                {
                    Character character = prefab.GetComponent<Character>();
                    if (character != null && !string.IsNullOrEmpty(character.m_name))
                    {
                        result = L(character.m_name);
                    }
                    else
                    {
                        ItemDrop itemDrop = prefab.GetComponent<ItemDrop>();
                        if (itemDrop != null && !string.IsNullOrEmpty(itemDrop.m_itemData.m_shared.m_name))
                        {
                            result = L(itemDrop.m_itemData.m_shared.m_name);
                        }
                    }
                }
            }
            catch (Exception)
            {
                result = prefabName;
            }

            PrettyNameCache[prefabName] = result;
            return result;
        }
        
        private static Text CreateText(Transform parent, string content, int fontSize, TextAnchor anchor)
        {
            GameObject go = new GameObject("Text");
            go.transform.SetParent(parent, false);

            Text text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = anchor;

            Font norseFont = GUIManager.Instance != null ? GUIManager.Instance.AveriaSerif : null;
            text.font = norseFont != null ? norseFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            text.color = new Color(0.15f, 0.09f, 0.05f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;

            return text;
        }

        private static void ApplyValheimHeaderStyle(Text text)
        {
            if (GUIManager.Instance == null) return;

            Font boldFont = GUIManager.Instance.AveriaSerifBold;
            if (boldFont != null)
            {
                text.font = boldFont;
            }

            text.color = GUIManager.Instance.ValheimOrange;
        }

        private static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, bool valheimStyle = true)
        {
            GameObject go = new GameObject("Button");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>();

            Button button = go.AddComponent<Button>();
            button.onClick.AddListener(onClick);

            Text text = CreateText(go.transform, label, 12, TextAnchor.MiddleCenter);
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            
            if (valheimStyle && GUIManager.Instance != null)
            {
                GUIManager.Instance.ApplyButtonStyle(button);
            }
            else
            {
                Image img = go.GetComponent<Image>();

                Sprite btnSprite = GUIManager.Instance != null ? GUIManager.Instance.GetSprite("button") : null;
                if (btnSprite != null)
                {
                    img.sprite = btnSprite;
                    img.type = Image.Type.Sliced;
                }
                else
                {
                    img.sprite = null;
                    img.color = new Color(0.35f, 0.24f, 0.14f, 0.95f);
                }
                
                ColorBlock cb = button.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
                cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
                button.colors = cb;
                
                text.fontSize = (label != null && label.Length <= 2) ? 18 : 12;
                if (GUIManager.Instance != null)
                {
                    Font bold = GUIManager.Instance.AveriaSerifBold;
                    if (bold != null) text.font = bold;
                    text.color = GUIManager.Instance.ValheimOrange;
                }
            }

            return button;
        }
    }
}