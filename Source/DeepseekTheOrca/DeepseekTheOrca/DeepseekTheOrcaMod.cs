using UnityEngine;
using Verse;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DeepseekTheOrca
{
    internal sealed class OrcaPluginDescriptor
    {
        public readonly string id;
        public readonly bool defaultEnabled;
        public readonly string prompt;
        public readonly List<string> triggerHints;
        public readonly List<string> allowedTools;
        public readonly string sourceMod;
        private readonly bool translateText;
        private readonly string labelText;
        private readonly string categoryText;
        private readonly string descriptionText;
        private readonly string detailsText;
        private readonly string enableLabelText;
        private readonly string enableTooltipText;

        public OrcaPluginDescriptor(string id, string labelKey, string categoryKey, string descriptionKey, string detailsKey, string enableLabelKey, string enableTooltipKey)
        {
            this.id = id;
            translateText = true;
            labelText = labelKey;
            categoryText = categoryKey;
            descriptionText = descriptionKey;
            detailsText = detailsKey;
            enableLabelText = enableLabelKey;
            enableTooltipText = enableTooltipKey;
            defaultEnabled = true;
            prompt = "";
            triggerHints = new List<string>();
            allowedTools = new List<string>();
            sourceMod = "Core";
        }

        public OrcaPluginDescriptor(OrcaPluginDef def)
        {
            id = DeepseekTheOrcaMod.OrcaPluginDefPrefix + def.defName;
            translateText = false;
            labelText = def.label.NullOrEmpty() ? def.defName : def.LabelCap.ToString();
            categoryText = def.category ?? "";
            descriptionText = def.description ?? "";
            detailsText = def.details ?? "";
            enableLabelText = "Enable " + labelText;
            enableTooltipText = descriptionText;
            defaultEnabled = def.defaultEnabled;
            prompt = def.prompt ?? "";
            triggerHints = CleanList(def.triggerHints);
            allowedTools = CleanList(def.allowedTools);
            sourceMod = def.modContentPack == null ? "" : def.modContentPack.Name;
        }

        public string Label
        {
            get { return Text(labelText); }
        }

        public string Category
        {
            get { return Text(categoryText); }
        }

        public string Description
        {
            get { return Text(descriptionText); }
        }

        public string Details
        {
            get { return Text(detailsText); }
        }

        public string EnableLabel
        {
            get { return Text(enableLabelText); }
        }

        public string EnableTooltip
        {
            get { return Text(enableTooltipText); }
        }

        private string Text(string value)
        {
            if (value.NullOrEmpty())
            {
                return "";
            }

            return translateText ? value.Translate().ToString() : value;
        }

        private static List<string> CleanList(List<string> values)
        {
            if (values == null)
            {
                return new List<string>();
            }

            return values.Select(value => value == null ? "" : value.Trim())
                .Where(value => !value.NullOrEmpty())
                .Distinct()
                .ToList();
        }
    }

    public sealed class DeepseekTheOrcaMod : Mod
    {
        private enum SettingsPage
        {
            ApiKeys,
            Llm,
            Personas,
            Skills,
            Plugins,
            Tools,
            Mcp,
            Debug
        }

        public static DeepseekTheOrcaMod Instance;
        public static DeepseekTheOrcaSettings Settings;

        private SettingsPage selectedPage = SettingsPage.Llm;
        private string maxToolCallsBuffer;
        private string planningMtbDaysBuffer;
        private string tavilyMaxResultsBuffer;
        private string httpMcpMaxResultCharsBuffer;
        private string colonyObservationProactiveChanceBuffer;
        private string rimtalkProactiveBaseChanceBuffer;
        private string rimtalkProactiveMissBonusBuffer;
        private string rimtalkProactiveForceAfterMissesBuffer;
        private Vector2 rightScrollPosition;
        private string selectedPluginId = MoodPluginId;

        private static readonly Color PanelFill = new Color(0.05f, 0.055f, 0.065f, 0.72f);
        private static readonly Color RowSelectedFill = new Color(0.18f, 0.23f, 0.28f, 0.86f);
        private static readonly Color RowHoverFill = new Color(0.12f, 0.14f, 0.16f, 0.72f);
        private static readonly Color BorderColor = new Color(0.42f, 0.45f, 0.48f, 1f);
        private const string MoodPluginId = "mood";
        private const string ProactivePluginId = "ambient_proactive_dialogue";
        internal const string OrcaPluginDefPrefix = "def:";
        private static readonly List<OrcaPluginDescriptor> BuiltInPluginDescriptors = new List<OrcaPluginDescriptor>
        {
            new OrcaPluginDescriptor(
                MoodPluginId,
                "DTO_MoodPluginName",
                "DTO_MoodPluginCategory",
                "DTO_MoodPluginDescription",
                "DTO_MoodPluginDetails",
                "DTO_EnableMoodPlugin",
                "DTO_EnableMoodPluginTooltip"),
            new OrcaPluginDescriptor(
                ProactivePluginId,
                "DTO_ProactivePluginName",
                "DTO_ProactivePluginCategory",
                "DTO_ProactivePluginDescription",
                "DTO_ProactivePluginDetails",
                "DTO_EnableAmbientProactiveDialogue",
                "DTO_EnableAmbientProactiveDialogueTooltip")
        };

        public DeepseekTheOrcaMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<DeepseekTheOrcaSettings>();
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                OrcaStorytellerAppearance.ApplyCurrent();
            });
        }

        public override string SettingsCategory()
        {
            return "Deepseek The Orca";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.EnsureLlmConnections();
            OrcaLlmConnectionManager.Tick(Settings);

            float gap = 12f;
            float leftWidth = Mathf.Max(180f, inRect.width * 0.24f);
            Rect left = new Rect(inRect.x, inRect.y, leftWidth, inRect.height);
            Rect right = new Rect(left.xMax + gap, inRect.y, inRect.width - leftWidth - gap, inRect.height);

            DrawPanel(left);
            DrawPanel(right);
            DrawNavigation(left.ContractedBy(10f));
            DrawSelectedSettingsPage(right.ContractedBy(10f));
        }

        private void DrawNavigation(Rect rect)
        {
            Text.Font = GameFont.Small;
            float y = rect.y;
            DrawPageButton(new Rect(rect.x, y, rect.width, 34f), "DTO_SettingsPageApiKeys".Translate(), SettingsPage.ApiKeys);
            y += 42f;
            DrawPageButton(new Rect(rect.x, y, rect.width, 34f), "DTO_SettingsPageLlm".Translate(), SettingsPage.Llm);
            y += 42f;
            DrawPageButton(new Rect(rect.x, y, rect.width, 34f), "DTO_SettingsPagePersonas".Translate(), SettingsPage.Personas);
            y += 42f;
            DrawPageButton(new Rect(rect.x, y, rect.width, 34f), "DTO_SettingsPageSkills".Translate(), SettingsPage.Skills);
            y += 42f;
            DrawPageButton(new Rect(rect.x, y, rect.width, 34f), "DTO_SettingsPagePlugins".Translate(), SettingsPage.Plugins);
            y += 42f;
            DrawPageButton(new Rect(rect.x, y, rect.width, 34f), "DTO_SettingsPageTools".Translate(), SettingsPage.Tools);
            y += 42f;
            DrawPageButton(new Rect(rect.x, y, rect.width, 34f), "DTO_SettingsPageMcp".Translate(), SettingsPage.Mcp);
            y += 42f;
            DrawPageButton(new Rect(rect.x, y, rect.width, 34f), "DTO_SettingsPageDebug".Translate(), SettingsPage.Debug);
        }

        private void DrawPageButton(Rect rect, string label, SettingsPage page)
        {
            if (Widgets.ButtonText(rect, label, selectedPage == page))
            {
                selectedPage = page;
                rightScrollPosition = Vector2.zero;
            }
        }

        private void DrawSelectedSettingsPage(Rect rect)
        {
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, 920f);
            Widgets.BeginScrollView(rect, ref rightScrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            switch (selectedPage)
            {
                case SettingsPage.ApiKeys:
                    DrawApiKeySettings(listing);
                    break;
                case SettingsPage.Tools:
                    DrawToolsSettings(listing);
                    break;
                case SettingsPage.Personas:
                    DrawPersonaSettings(listing);
                    break;
                case SettingsPage.Skills:
                    DrawSkillSettings(listing);
                    break;
                case SettingsPage.Plugins:
                    DrawPluginSettings(listing);
                    break;
                case SettingsPage.Mcp:
                    DrawMcpSettings(listing);
                    break;
                case SettingsPage.Debug:
                    DrawDebugSettings(listing);
                    break;
                default:
                    DrawLlmSettings(listing);
                    break;
            }

            listing.End();
            Widgets.EndScrollView();
        }

        private void DrawLlmSettings(Listing_Standard listing)
        {
            listing.CheckboxLabeled("DTO_EnableAiPlanning".Translate(), ref Settings.enableAiPlanning, "DTO_EnableAiPlanningTooltip".Translate());
            listing.GapLine();
            DrawModelSelector(listing, "DTO_ModelFallback".Translate(), OrcaLlmModelRole.Fallback, "DTO_ModelFallbackTooltip".Translate(), allowFallback: false);
            DrawModelSelector(listing, "DTO_ModelController".Translate(), OrcaLlmModelRole.Controller, "DTO_ModelControllerTooltip".Translate(), allowFallback: true);
            DrawModelSelector(listing, "DTO_ModelDecision".Translate(), OrcaLlmModelRole.Decision, "DTO_ModelDecisionTooltip".Translate(), allowFallback: true);
            DrawModelSelector(listing, "DTO_ModelDialogue".Translate(), OrcaLlmModelRole.Dialogue, "DTO_ModelDialogueTooltip".Translate(), allowFallback: true);
            DrawModelSelector(listing, "DTO_ModelTool".Translate(), OrcaLlmModelRole.Tool, "DTO_ModelToolTooltip".Translate(), allowFallback: true);
            DrawModelSelector(listing, "DTO_ModelVision".Translate(), OrcaLlmModelRole.Vision, "DTO_ModelVisionTooltip".Translate(), allowFallback: true);
            DrawModelSelector(listing, "DTO_ModelWebSearch".Translate(), OrcaLlmModelRole.WebSearch, "DTO_ModelWebSearchTooltip".Translate(), allowFallback: true);

            LlmConnectionStatus connectionStatus;
            string connectionMessage;
            LlmConnectionTester.Snapshot(out connectionStatus, out connectionMessage);
            listing.Label("DTO_ConnectionStatus".Translate() + ": " + ConnectionStatusText(connectionStatus));
            listing.Label(TranslateIfKey(connectionMessage));
            listing.Gap();
            listing.Label("DTO_OfflineNote".Translate());
        }

        private void DrawPersonaSettings(Listing_Standard listing)
        {
            DrawPersonaSelector(listing);
            listing.Gap();
            listing.Label("DTO_ChatPersonaFolder".Translate() + ": " + OrcaChatPersonaManager.PersonaFolderPath);
            listing.Label("DTO_ChatPersonaFormatNote".Translate());
        }

        private void DrawSkillSettings(Listing_Standard listing)
        {
            listing.Label("DTO_SkillManagerTitle".Translate());
            listing.Label("DTO_SkillManagerDescription".Translate());
            listing.Gap();
            if (listing.ButtonText("DTO_SkillManage".Translate()))
            {
                Find.WindowStack.Add(new OrcaSkillManagerWindow());
            }

            listing.Gap();
            List<OrcaSkillProfile> enabledSkills = OrcaSkillManager.EnabledSkills();
            listing.Label("DTO_SkillEnabledCount".Translate(enabledSkills.Count, OrcaSkillManager.AllSkills().Count));
            if (enabledSkills.Count == 0)
            {
                listing.Label("DTO_SkillNoEnabled".Translate());
            }
            else
            {
                for (int i = 0; i < enabledSkills.Count; i++)
                {
                    OrcaSkillProfile skill = enabledSkills[i];
                    listing.Label("- " + skill.label + (skill.description.NullOrEmpty() ? "" : ": " + skill.description));
                }
            }

            listing.Gap();
            listing.Label("DTO_SkillFolder".Translate() + ": " + OrcaSkillManager.SkillFolderPath);
            listing.Label("DTO_SkillFormatNote".Translate());
        }

        private void DrawApiKeySettings(Listing_Standard listing)
        {
            Settings.EnsureLlmConnections();
            if (listing.ButtonText("DTO_ApiKeyAddConnection".Translate()))
            {
                Settings.AddLlmConnection();
            }

            if (Settings.llmConnections.Count == 0)
            {
                listing.Label("DTO_ApiKeyNoConnections".Translate());
                return;
            }

            for (int i = 0; i < Settings.llmConnections.Count; i++)
            {
                OrcaLlmConnectionSettings connection = Settings.llmConnections[i];
                if (connection == null)
                {
                    continue;
                }

                listing.GapLine();
                Rect headerRect = listing.GetRect(32f);
                Widgets.Label(new Rect(headerRect.x, headerRect.y + 6f, headerRect.width - 130f, 24f), connection.name + " (" + LlmProviderConfig.Profile(connection.provider).label + ")");
                Rect removeRect = new Rect(headerRect.xMax - 120f, headerRect.y, 120f, 30f);
                if (Widgets.ButtonText(removeRect, "DTO_ApiKeyRemoveConnection".Translate()))
                {
                    Settings.llmConnections.RemoveAt(i);
                    i--;
                    continue;
                }

                bool oldEnabled = connection.enabled;
                listing.CheckboxLabeled("DTO_ApiKeyConnectionEnabled".Translate(), ref connection.enabled);
                if (connection.enabled != oldEnabled)
                {
                    connection.MarkDirty();
                }

                listing.Label("DTO_ApiKeyConnectionName".Translate());
                string oldName = connection.name;
                connection.name = listing.TextEntry(connection.name ?? "");
                if (connection.name != oldName)
                {
                    connection.Normalize();
                }

                LlmProviderProfile profile = LlmProviderConfig.Profile(connection.provider);
                if (listing.ButtonText("DTO_ApiProvider".Translate() + ": " + profile.label))
                {
                    connection.provider = LlmProviderConfig.NextProvider(connection.provider);
                    connection.MarkDirty();
                }

                if (connection.provider == LlmProviderConfig.Custom)
                {
                    listing.Label("DTO_CustomBaseUrl".Translate(), -1f, "DTO_CustomBaseUrlTooltip".Translate());
                    string oldBaseUrl = connection.customBaseUrl;
                    connection.customBaseUrl = listing.TextEntry(connection.customBaseUrl ?? "");
                    if (connection.customBaseUrl != oldBaseUrl)
                    {
                        connection.MarkDirty();
                    }
                }

                listing.Label("DTO_ActiveBaseUrl".Translate() + ": " + (connection.ActiveBaseUrl.NullOrEmpty() ? "-" : connection.ActiveBaseUrl));
                listing.Label("DTO_ApiKey".Translate(), -1f, "DTO_ApiKeyTooltip".Translate());
                string oldApiKey = connection.apiKey;
                connection.apiKey = listing.TextEntry(connection.apiKey ?? "");
                if (connection.apiKey != oldApiKey)
                {
                    connection.MarkDirty();
                }

                if (connection.provider == LlmProviderConfig.OpenAI)
                {
                    listing.Label("DTO_OpenAiOrganization".Translate(), -1f, "DTO_OpenAiOrganizationTooltip".Translate());
                    string oldOrganization = connection.openAiOrganization;
                    connection.openAiOrganization = listing.TextEntry(connection.openAiOrganization ?? "");
                    if (connection.openAiOrganization != oldOrganization)
                    {
                        connection.MarkDirty();
                    }

                    listing.Label("DTO_OpenAiProject".Translate(), -1f, "DTO_OpenAiProjectTooltip".Translate());
                    string oldProject = connection.openAiProject;
                    connection.openAiProject = listing.TextEntry(connection.openAiProject ?? "");
                    if (connection.openAiProject != oldProject)
                    {
                        connection.MarkDirty();
                    }
                }

                listing.Label("DTO_ApiProxyUrl".Translate(), -1f, "DTO_ApiProxyUrlTooltip".Translate());
                string oldProxyUrl = connection.proxyUrl;
                connection.proxyUrl = listing.TextEntry(connection.proxyUrl ?? "");
                if (connection.proxyUrl != oldProxyUrl)
                {
                    connection.MarkDirty();
                }

                if (listing.ButtonText(OrcaLlmConnectionManager.IsTesting(connection) ? "DTO_TestConnectionRunning".Translate() : "DTO_ApiKeyRefreshModels".Translate()))
                {
                    connection.MarkDirty();
                    OrcaLlmConnectionManager.Start(connection);
                }

                listing.Label("DTO_ConnectionStatus".Translate() + ": " + ConnectionStatusText(connection.status));
                listing.Label(TranslateIfKey(connection.message));
                listing.Label("DTO_ApiKeyAvailableModels".Translate() + ": " + AvailableModelsText(connection));
            }
        }

        private void DrawToolsSettings(Listing_Standard listing)
        {
            listing.CheckboxLabeled("DTO_EnableWebSearch".Translate(), ref Settings.enableWebSearch, "DTO_EnableWebSearchTooltip".Translate());
            if (Settings.enableWebSearch)
            {
                Settings.webSearchMode = "tavily";
                listing.Gap(4f);
                listing.Label("DTO_WebSearchProvider".Translate() + ": " + "DTO_WebSearchProviderTavily".Translate(), -1f, "DTO_WebSearchProviderTooltip".Translate());
                listing.Label("DTO_TavilyApiKey".Translate(), -1f, "DTO_TavilyApiKeyTooltip".Translate());
                Settings.tavilyApiKey = listing.TextEntry(Settings.tavilyApiKey ?? "");
                listing.TextFieldNumericLabeled("DTO_TavilyMaxResults".Translate(), ref Settings.tavilyMaxResults, ref tavilyMaxResultsBuffer, 1, 10);
                if (listing.ButtonText("DTO_TavilySearchDepth".Translate() + ": " + Settings.tavilySearchDepth))
                {
                    Settings.tavilySearchDepth = NextTavilySearchDepth(Settings.tavilySearchDepth);
                }
                listing.Label("DTO_WebSearchNativeReserved".Translate());
            }

            listing.GapLine();
            listing.TextFieldNumericLabeled("DTO_MaxToolCalls".Translate(), ref Settings.maxToolCalls, ref maxToolCallsBuffer, 1, 32);
            listing.TextFieldNumericLabeled("DTO_PlanningMtbDays".Translate(), ref Settings.planningMtbDays, ref planningMtbDaysBuffer, 0.1f, 60f);
        }

        private void DrawPluginSettings(Listing_Standard listing)
        {
            listing.Label("DTO_PluginManagerTitle".Translate());
            listing.Label("DTO_PluginManagerDescription".Translate());
            listing.Gap(8f);

            Rect managerRect = listing.GetRect(560f);
            DrawPluginManager(managerRect);

            listing.Gap();
            listing.Label("DTO_PluginManagerFutureNote".Translate());
        }

        private void DrawPluginManager(Rect rect)
        {
            List<OrcaPluginDescriptor> plugins = AllPluginDescriptors();
            if (plugins.Count == 0)
            {
                Widgets.Label(rect, "DTO_PluginManagerEmpty".Translate());
                return;
            }

            OrcaPluginDescriptor selected = plugins.FirstOrDefault(p => p.id == selectedPluginId) ?? plugins[0];
            selectedPluginId = selected.id;

            float gap = 10f;
            float leftWidth = Mathf.Min(260f, Mathf.Max(210f, rect.width * 0.38f));
            Rect listRect = new Rect(rect.x, rect.y, leftWidth, rect.height);
            Rect detailRect = new Rect(listRect.xMax + gap, rect.y, rect.width - leftWidth - gap, rect.height);

            DrawPanel(listRect);
            DrawPanel(detailRect);
            DrawPluginList(listRect.ContractedBy(8f), plugins);
            DrawPluginDetails(detailRect.ContractedBy(12f), selected);
        }

        private void DrawPluginList(Rect rect, List<OrcaPluginDescriptor> plugins)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), "DTO_PluginManagerInstalled".Translate());

            float y = rect.y + 32f;
            for (int i = 0; i < plugins.Count; i++)
            {
                OrcaPluginDescriptor plugin = plugins[i];
                Rect row = new Rect(rect.x, y, rect.width, 68f);
                bool selected = plugin.id == selectedPluginId;
                if (selected)
                {
                    Widgets.DrawBoxSolid(row, RowSelectedFill);
                }
                else if (Mouse.IsOver(row))
                {
                    Widgets.DrawBoxSolid(row, RowHoverFill);
                }

                if (Widgets.ButtonInvisible(row))
                {
                    selectedPluginId = plugin.id;
                }

                string state = PluginEnabled(plugin) ? "DTO_PluginStateEnabled".Translate().ToString() : "DTO_PluginStateDisabled".Translate().ToString();
                Widgets.Label(new Rect(row.x + 8f, row.y + 7f, row.width - 16f, 24f), plugin.Label);
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(row.x + 8f, row.y + 29f, row.width - 16f, 22f), state);
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(row.x + 8f, row.y + 47f, row.width - 16f, 20f), PluginSourceText(plugin));

                TooltipHandler.TipRegion(row, plugin.Description);
                y += 74f;
            }
        }

        private void DrawPluginDetails(Rect rect, OrcaPluginDescriptor plugin)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f), plugin.Label);
            Text.Font = GameFont.Small;

            float y = rect.y + 38f;
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "DTO_PluginCategory".Translate() + ": " + plugin.Category);
            y += 30f;
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f), PluginSourceText(plugin));
            y += 30f;

            bool enabled = PluginEnabled(plugin);
            bool oldEnabled = enabled;
            Rect checkboxRect = new Rect(rect.x, y, rect.width, 28f);
            Widgets.CheckboxLabeled(checkboxRect, plugin.EnableLabel, ref enabled, false, null, null, false);
            TooltipHandler.TipRegion(checkboxRect, plugin.EnableTooltip);
            if (enabled != oldEnabled)
            {
                SetPluginEnabled(plugin, enabled);
            }
            y += 38f;

            Rect line = new Rect(rect.x, y, rect.width, 1f);
            Widgets.DrawBoxSolid(line, BorderColor);
            y += 12f;

            Widgets.Label(new Rect(rect.x, y, rect.width, 64f), plugin.Description);
            y += 74f;
            Widgets.Label(new Rect(rect.x, y, rect.width, 120f), plugin.Details);
            y += 112f;

            if (plugin.id == ProactivePluginId)
            {
                DrawProactivePluginControls(new Rect(rect.x, y, rect.width, rect.yMax - y));
            }
        }

        private void DrawProactivePluginControls(Rect rect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);
            listing.Label("DTO_ProactivePluginSettings".Translate());
            listing.TextFieldNumericLabeled("DTO_ColonyObservationProactiveChance".Translate(), ref Settings.colonyObservationProactiveChance, ref colonyObservationProactiveChanceBuffer, 0f, 1f);
            listing.TextFieldNumericLabeled("DTO_RimtalkProactiveBaseChance".Translate(), ref Settings.rimtalkProactiveBaseChance, ref rimtalkProactiveBaseChanceBuffer, 0f, 1f);
            listing.TextFieldNumericLabeled("DTO_RimtalkProactiveMissBonus".Translate(), ref Settings.rimtalkProactiveMissBonus, ref rimtalkProactiveMissBonusBuffer, 0f, 1f);
            listing.TextFieldNumericLabeled("DTO_RimtalkProactiveForceAfterMisses".Translate(), ref Settings.rimtalkProactiveForceAfterMisses, ref rimtalkProactiveForceAfterMissesBuffer, 1, 20);
            listing.Label("DTO_ProactivePluginSettingsNote".Translate());
            listing.End();
        }

        private static List<OrcaPluginDescriptor> AllPluginDescriptors()
        {
            List<OrcaPluginDescriptor> result = new List<OrcaPluginDescriptor>();
            result.AddRange(BuiltInPluginDescriptors);
            List<OrcaPluginDef> defs = DefDatabase<OrcaPluginDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                OrcaPluginDef def = defs[i];
                if (def != null && !def.defName.NullOrEmpty())
                {
                    result.Add(new OrcaPluginDescriptor(def));
                }
            }

            return result.OrderBy(plugin => plugin.Label).ToList();
        }

        private static bool PluginEnabled(OrcaPluginDescriptor plugin)
        {
            if (plugin == null)
            {
                return false;
            }

            string id = plugin.id;
            switch (id)
            {
                case MoodPluginId:
                    return Settings == null || Settings.enableMoodPlugin;
                case ProactivePluginId:
                    return Settings == null || Settings.enableAmbientProactiveDialogue;
                default:
                    if (id != null && id.StartsWith(OrcaPluginDefPrefix, System.StringComparison.Ordinal))
                    {
                        return Settings == null
                            ? plugin.defaultEnabled
                            : Settings.IsDefPluginEnabled(id.Substring(OrcaPluginDefPrefix.Length), plugin.defaultEnabled);
                    }
                    return plugin.defaultEnabled;
            }
        }

        private static void SetPluginEnabled(OrcaPluginDescriptor plugin, bool enabled)
        {
            if (plugin == null)
            {
                return;
            }

            string id = plugin.id;
            switch (id)
            {
                case MoodPluginId:
                    Settings.enableMoodPlugin = enabled;
                    OrcaChatWindowManager.Session.Clear();
                    break;
                case ProactivePluginId:
                    Settings.enableAmbientProactiveDialogue = enabled;
                    break;
                default:
                    if (id != null && id.StartsWith(OrcaPluginDefPrefix, System.StringComparison.Ordinal) && Settings != null)
                    {
                        Settings.SetDefPluginEnabled(id.Substring(OrcaPluginDefPrefix.Length), enabled, plugin.defaultEnabled);
                        OrcaChatWindowManager.Session.Clear();
                    }
                    break;
            }
        }

        public static string FormatEnabledPluginPrompt()
        {
            List<OrcaPluginDescriptor> plugins = AllPluginDescriptors()
                .Where(plugin => plugin.id != MoodPluginId && plugin.id != ProactivePluginId && PluginEnabled(plugin) && !plugin.prompt.NullOrEmpty())
                .ToList();
            if (plugins.Count == 0)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Enabled plugin modules:");
            builder.AppendLine("Plugins may add broad behavior, formatting, or runtime context instructions. Treat them as lower priority than safety, game validity, persona, and direct player intent.");
            for (int i = 0; i < plugins.Count; i++)
            {
                OrcaPluginDescriptor plugin = plugins[i];
                builder.AppendLine();
                builder.AppendLine("Plugin: " + SafeLine(plugin.Label));
                if (!plugin.Category.NullOrEmpty())
                {
                    builder.AppendLine("Category: " + SafeLine(plugin.Category));
                }
                if (!plugin.Description.NullOrEmpty())
                {
                    builder.AppendLine("Description: " + SafeLine(plugin.Description));
                }
                if (plugin.allowedTools != null && plugin.allowedTools.Count > 0)
                {
                    builder.AppendLine("Allowed/recommended tools: " + string.Join(", ", plugin.allowedTools.ToArray()));
                }
                builder.AppendLine("Instructions:");
                builder.AppendLine(plugin.prompt.Trim());
            }

            return builder.ToString().TrimEnd();
        }

        public static string FormatPluginControllerRoutingHint()
        {
            List<OrcaPluginDescriptor> plugins = AllPluginDescriptors()
                .Where(plugin => plugin.id != MoodPluginId && plugin.id != ProactivePluginId && PluginEnabled(plugin))
                .ToList();
            if (plugins.Count == 0)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(" Enabled plugin modules may affect routing. Plugins: ");
            for (int i = 0; i < plugins.Count; i++)
            {
                OrcaPluginDescriptor plugin = plugins[i];
                if (i > 0)
                {
                    builder.Append("; ");
                }
                builder.Append(SafeLine(plugin.Label));
                if (plugin.triggerHints != null && plugin.triggerHints.Count > 0)
                {
                    builder.Append(" triggers=");
                    builder.Append(string.Join(",", plugin.triggerHints.ToArray()));
                }
                if (plugin.allowedTools != null && plugin.allowedTools.Count > 0)
                {
                    builder.Append(" tools=");
                    builder.Append(string.Join(",", plugin.allowedTools.ToArray()));
                }
            }

            return builder.ToString();
        }

        private static string SafeLine(string value)
        {
            return (value ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string PluginSourceText(OrcaPluginDescriptor plugin)
        {
            string source = plugin == null ? "" : plugin.sourceMod;
            if (source.NullOrEmpty())
            {
                source = "DTO_ExtensionSourceLocal".Translate().ToString();
            }

            return "DTO_ExtensionSource".Translate() + ": " + source;
        }

        private void DrawMcpSettings(Listing_Standard listing)
        {
            listing.CheckboxLabeled("DTO_EnableHttpMcp".Translate(), ref Settings.enableHttpMcp, "DTO_EnableHttpMcpTooltip".Translate());
            listing.TextFieldNumericLabeled("DTO_HttpMcpMaxResultChars".Translate(), ref Settings.httpMcpMaxResultChars, ref httpMcpMaxResultCharsBuffer, 500, 20000);
            listing.Gap();

            if (Settings.httpMcpServers == null)
            {
                Settings.httpMcpServers = new System.Collections.Generic.List<OrcaHttpMcpServerSettings>();
            }

            if (listing.ButtonText("DTO_HttpMcpAddServer".Translate()))
            {
                Settings.httpMcpServers.Add(new OrcaHttpMcpServerSettings
                {
                    name = "MCP " + (Settings.httpMcpServers.Count + 1),
                    enabled = true
                });
            }

            if (Settings.httpMcpServers.Count == 0)
            {
                listing.Label("DTO_HttpMcpNoServers".Translate());
            }

            for (int i = 0; i < Settings.httpMcpServers.Count; i++)
            {
                OrcaHttpMcpServerSettings server = Settings.httpMcpServers[i];
                if (server == null)
                {
                    continue;
                }

                listing.GapLine();
                Rect headerRect = listing.GetRect(32f);
                Widgets.Label(new Rect(headerRect.x, headerRect.y + 6f, headerRect.width - 130f, 24f), "DTO_HttpMcpServer".Translate() + " " + (i + 1));
                Rect removeRect = new Rect(headerRect.xMax - 120f, headerRect.y, 120f, 30f);
                if (Widgets.ButtonText(removeRect, "DTO_HttpMcpRemoveServer".Translate()))
                {
                    Settings.httpMcpServers.RemoveAt(i);
                    i--;
                    continue;
                }

                listing.CheckboxLabeled("DTO_HttpMcpServerEnabled".Translate(), ref server.enabled);
                listing.Label("DTO_HttpMcpServerName".Translate());
                server.name = listing.TextEntry(server.name ?? "");
                listing.Label("DTO_HttpMcpUrl".Translate(), -1f, "DTO_HttpMcpUrlTooltip".Translate());
                server.url = listing.TextEntry(server.url ?? "");
                listing.Label("DTO_HttpMcpBearerToken".Translate(), -1f, "DTO_HttpMcpBearerTokenTooltip".Translate());
                server.bearerToken = listing.TextEntry(server.bearerToken ?? "");
            }

            listing.Gap();
            listing.Label("DTO_HttpMcpNote".Translate());
        }

        private void DrawDebugSettings(Listing_Standard listing)
        {
            listing.CheckboxLabeled("DTO_DebugLogging".Translate(), ref Settings.debugLogging, "DTO_DebugLoggingTooltip".Translate());
            listing.Gap();
            listing.Label("DTO_DebugTabTitle".Translate());
            listing.Label("DTO_DebugToolChainTab".Translate());
            listing.Label("DTO_DebugSingleToolTab".Translate());
        }

        private static void DrawPersonaSelector(Listing_Standard listing)
        {
            listing.Label("DTO_ChatPersona".Translate(), -1f, "DTO_ChatPersonaTooltip".Translate());
            OrcaChatPersonaProfile selected = OrcaChatPersonaManager.Get(Settings.chatPersonaDefName);
            listing.Label(PersonaSummary(selected));

            if (listing.ButtonText("DTO_ChatPersonaManage".Translate()))
            {
                Find.WindowStack.Add(new OrcaPersonaManagerWindow());
            }
        }

        private static string PersonaSummary(OrcaChatPersonaProfile profile)
        {
            if (profile == null)
            {
                return "DTO_ChatPersonaMissing".Translate();
            }

            string source = profile.sourceMod;
            if (source.NullOrEmpty())
            {
                source = "DTO_ExtensionSourceLocal".Translate().ToString();
            }

            return profile.label + " (" + source + ")";
        }

        private static void DrawModelSelector(Listing_Standard listing, TaggedString label, OrcaLlmModelRole role, string tooltip, bool allowFallback)
        {
            listing.Label(label, -1f, tooltip);
            string value = Settings.ModelReferenceForRole(role);
            string buttonLabel = value.NullOrEmpty() && allowFallback
                ? "DTO_ModelUseFallback".Translate().ToString()
                : Settings.ModelReferenceLabel(value);
            if (!listing.ButtonText(buttonLabel))
            {
                return;
            }

            List<OrcaModelOption> modelOptions = Settings.AvailableModelOptions();
            Find.WindowStack.Add(new OrcaModelSelectionWindow(role, allowFallback, modelOptions));
        }

        public static void SetModelReferenceForRole(OrcaLlmModelRole role, string value)
        {
            switch (role)
            {
                case OrcaLlmModelRole.Controller:
                    Settings.controllerModel = value;
                    break;
                case OrcaLlmModelRole.Decision:
                    Settings.decisionModel = value;
                    break;
                case OrcaLlmModelRole.Dialogue:
                    Settings.dialogueModel = value;
                    break;
                case OrcaLlmModelRole.Tool:
                    Settings.toolModel = value;
                    break;
                case OrcaLlmModelRole.Vision:
                    Settings.visionModel = value;
                    break;
                case OrcaLlmModelRole.WebSearch:
                    Settings.webSearchModel = value;
                    break;
                default:
                    Settings.model = value;
                    break;
            }
        }

        private static string AvailableModelsText(OrcaLlmConnectionSettings connection)
        {
            if (connection == null || connection.availableModels == null || connection.availableModels.Count == 0)
            {
                return "DTO_ModelNoDiscoveredModels".Translate();
            }

            int count = connection.availableModels.Count;
            int shown = Mathf.Min(5, count);
            List<string> names = new List<string>();
            for (int i = 0; i < shown; i++)
            {
                names.Add(connection.availableModels[i]);
            }

            string text = string.Join(", ", names.ToArray());
            if (count > shown)
            {
                text += " +" + (count - shown);
            }

            return text;
        }

        private static void DrawPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelFill);
            DrawOutline(rect);
        }

        private static void DrawOutline(Rect rect)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 1f), BorderColor);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), BorderColor);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 1f, rect.height), BorderColor);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), BorderColor);
        }

        private static string ConnectionStatusText(LlmConnectionStatus status)
        {
            switch (status)
            {
                case LlmConnectionStatus.Testing:
                    return "DTO_ConnectionStatusTesting".Translate();
                case LlmConnectionStatus.Succeeded:
                    return "DTO_ConnectionStatusSucceeded".Translate();
                case LlmConnectionStatus.Failed:
                    return "DTO_ConnectionStatusFailed".Translate();
                default:
                    return "DTO_ConnectionStatusNotTested".Translate();
            }
        }

        private static string ConnectionStatusText(string status)
        {
            switch (status)
            {
                case "testing":
                    return "DTO_ConnectionStatusTesting".Translate();
                case "succeeded":
                    return "DTO_ConnectionStatusSucceeded".Translate();
                case "failed":
                    return "DTO_ConnectionStatusFailed".Translate();
                default:
                    return "DTO_ConnectionStatusNotTested".Translate();
            }
        }

        private static string TranslateIfKey(string text)
        {
            if (text == "DTO_ConnectionNotTested" || text == "DTO_ConnectionTesting")
            {
                return text.Translate();
            }

            return text ?? "";
        }

        private static string NextTavilySearchDepth(string current)
        {
            switch (current)
            {
                case "basic":
                    return "advanced";
                case "advanced":
                    return "fast";
                case "fast":
                    return "ultra-fast";
                default:
                    return "basic";
            }
        }
    }
}
