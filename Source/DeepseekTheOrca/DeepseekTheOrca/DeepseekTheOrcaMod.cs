using UnityEngine;
using Verse;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DeepseekTheOrca.Rimtalk;

namespace DeepseekTheOrca
{
    internal sealed class OrcaExtensionDescriptor
    {
        public readonly string id;
        public readonly bool defaultEnabled;
        public readonly string sourceMod;
        public readonly OrcaExtensionDef extensionDef;
        private readonly bool translateText;
        private readonly string labelText;
        private readonly string categoryText;
        private readonly string descriptionText;
        private readonly string detailsText;
        private readonly string enableLabelText;
        private readonly string enableTooltipText;

        public OrcaExtensionDescriptor(OrcaExtensionDef def)
        {
            id = def.defName;
            translateText = false;
            labelText = def.label.NullOrEmpty() ? def.defName : def.label;
            categoryText = def.category.NullOrEmpty() ? "Extension" : def.category;
            descriptionText = def.description ?? "";
            detailsText = def.details ?? "";
            enableLabelText = "";
            enableTooltipText = descriptionText;
            defaultEnabled = def.defaultEnabled;
            sourceMod = IsCoreMod(def.modContentPack) ? "Core" : def.modContentPack == null ? "" : def.modContentPack.Name;
            extensionDef = def;
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
            get { return enableLabelText.NullOrEmpty() ? "DTO_EnableExtension".Translate(Label).ToString() : Text(enableLabelText); }
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

            return translateText || value.StartsWith("DTO_") ? value.Translate().ToString() : value;
        }

        private static bool IsCoreMod(ModContentPack mod)
        {
            return mod != null
                && DeepseekTheOrcaMod.Instance != null
                && DeepseekTheOrcaMod.Instance.Content != null
                && mod.PackageId == DeepseekTheOrcaMod.Instance.Content.PackageId;
        }

    }

    public sealed class OrcaSettingsWindow : Window
    {
        private readonly DeepseekTheOrcaMod mod;

        public OrcaSettingsWindow(DeepseekTheOrcaMod mod)
        {
            this.mod = mod;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
            resizeable = true;
            onlyOneOfTypeAllowed = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(980f, 720f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (mod == null)
            {
                Close();
                return;
            }

            mod.DrawSettingsUi(inRect);
        }
    }

    public sealed class DeepseekTheOrcaMod : Mod
    {
        public const string DisplayName = "RimAgent";

        private enum SettingsPage
        {
            Providers,
            AgentModels,
            Personas,
            Skills,
            Plugins,
            Tools,
            Mcp,
            Debug
        }

        public static DeepseekTheOrcaMod Instance;
        public static DeepseekTheOrcaSettings Settings;

        private SettingsPage selectedPage = SettingsPage.AgentModels;
        private string maxToolCallsBuffer;
        private string planningMtbDaysBuffer;
        private string tavilyMaxResultsBuffer;
        private string httpMcpMaxResultCharsBuffer;
        private Vector2 rightScrollPosition;
        private Vector2 pluginDetailScrollPosition;
        private Vector2 providerListScrollPosition;
        private Vector2 providerDetailScrollPosition;
        private string selectedProviderId = "";
        private string providerModelFilter = "";
        private bool providerRemoveMode;
        private string selectedPluginId = "";

        private static readonly Color PanelFill = new Color(0.05f, 0.055f, 0.065f, 0.72f);
        private static readonly Color RowSelectedFill = new Color(0.18f, 0.23f, 0.28f, 0.86f);
        private static readonly Color RowHoverFill = new Color(0.12f, 0.14f, 0.16f, 0.72f);
        private static readonly Color BorderColor = new Color(0.42f, 0.45f, 0.48f, 1f);

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
            return DisplayName;
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (Find.WindowStack != null && !Find.WindowStack.IsOpen(typeof(OrcaSettingsWindow)))
            {
                Find.WindowStack.Add(new OrcaSettingsWindow(this));
            }

            if (Find.WindowStack != null)
            {
                Find.WindowStack.TryRemove(typeof(Dialog_ModSettings));
            }
        }

        internal void DrawSettingsUi(Rect inRect)
        {
            Settings.EnsureLlmConnections();
            OrcaLlmConnectionManager.Tick(Settings);
            OrcaHttpMcpClient.Tick();
            EnsureSelectedProvider();

            float gap = 12f;
            float leftWidth = Mathf.Clamp(inRect.width * 0.22f, 180f, 240f);
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
            DrawPageButton(new Rect(rect.x, y, rect.width, 34f), "DTO_SettingsPageProviders".Translate(), SettingsPage.Providers);
            y += 42f;
            DrawPageButton(new Rect(rect.x, y, rect.width, 34f), "DTO_SettingsPageAgentModels".Translate(), SettingsPage.AgentModels);
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
            bool selected = selectedPage == page;
            bool hover = Mouse.IsOver(rect);
            if (selected)
            {
                Widgets.DrawBoxSolid(rect, RowSelectedFill);
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), new Color(0.45f, 0.68f, 0.88f, 1f));
            }
            else if (hover)
            {
                Widgets.DrawBoxSolid(rect, RowHoverFill);
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 10f, rect.y, rect.width - 12f, rect.height), label);
            Text.Anchor = TextAnchor.UpperLeft;

            if (Widgets.ButtonInvisible(rect))
            {
                selectedPage = page;
                rightScrollPosition = Vector2.zero;
                pluginDetailScrollPosition = Vector2.zero;
            }
        }

        private void DrawSelectedSettingsPage(Rect rect)
        {
            if (selectedPage == SettingsPage.Providers)
            {
                DrawProviderSettings(rect);
                return;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, 1400f);
            Widgets.BeginScrollView(rect, ref rightScrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            switch (selectedPage)
            {
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
                    DrawAgentModelSettings(listing);
                    break;
            }

            listing.End();
            Widgets.EndScrollView();
        }

        private void DrawAgentModelSettings(Listing_Standard listing)
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

        private void DrawProviderSettings(Rect rect)
        {
            Settings.EnsureLlmConnections();
            EnsureSelectedProvider();

            float listWidth = Mathf.Min(260f, Mathf.Max(210f, rect.width * 0.34f));
            Rect listRect = new Rect(rect.x, rect.y, listWidth, rect.height);
            Rect detailRect = new Rect(listRect.xMax + 10f, rect.y, rect.width - listWidth - 10f, rect.height);

            DrawPanel(listRect);
            DrawPanel(detailRect);
            DrawProviderList(listRect.ContractedBy(8f));
            DrawProviderDetails(detailRect.ContractedBy(10f));
        }

        private void DrawProviderList(Rect rect)
        {
            Settings.EnsureLlmConnections();

            Rect toolbar = new Rect(rect.x, rect.y, rect.width, 32f);
            Rect addRect = new Rect(toolbar.x, toolbar.y, 34f, 30f);
            Rect removeModeRect = new Rect(addRect.xMax + 6f, toolbar.y, 34f, 30f);
            if (Widgets.ButtonText(addRect, "+"))
            {
                OrcaLlmConnectionSettings connection = Settings.AddLlmConnection();
                selectedProviderId = connection.id;
                providerRemoveMode = false;
            }
            if (Widgets.ButtonText(removeModeRect, "-", providerRemoveMode))
            {
                providerRemoveMode = !providerRemoveMode;
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(removeModeRect.xMax + 8f, toolbar.y, toolbar.width - 84f, 30f), providerRemoveMode ? "DTO_ProviderRemoveMode".Translate().ToString() : "DTO_ProviderListTitle".Translate().ToString());
            Text.Anchor = TextAnchor.UpperLeft;

            if (Settings.llmConnections.Count == 0)
            {
                Widgets.Label(new Rect(rect.x, toolbar.yMax + 8f, rect.width, 40f), "DTO_ApiKeyNoConnections".Translate());
                return;
            }

            float rowHeight = 46f;
            Rect outRect = new Rect(rect.x, toolbar.yMax + 8f, rect.width, rect.height - toolbar.height - 8f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Settings.llmConnections.Count * (rowHeight + 6f));
            Widgets.BeginScrollView(outRect, ref providerListScrollPosition, viewRect);
            for (int i = 0; i < Settings.llmConnections.Count; i++)
            {
                OrcaLlmConnectionSettings connection = Settings.llmConnections[i];
                if (connection == null)
                {
                    continue;
                }

                Rect row = new Rect(0f, i * (rowHeight + 6f), viewRect.width, rowHeight);
                DrawProviderListRow(row, connection);
            }
            Widgets.EndScrollView();
        }

        private void DrawProviderListRow(Rect row, OrcaLlmConnectionSettings connection)
        {
            bool selected = selectedProviderId == connection.id;
            bool hover = Mouse.IsOver(row);
            if (selected)
            {
                Widgets.DrawBoxSolid(row, RowSelectedFill);
                Widgets.DrawBoxSolid(new Rect(row.x, row.y, 3f, row.height), new Color(0.45f, 0.68f, 0.88f, 1f));
            }
            else if (hover)
            {
                Widgets.DrawBoxSolid(row, RowHoverFill);
            }

            string title = providerRemoveMode ? "[-] " + connection.name : connection.name;
            string subtitle = LlmProviderConfig.Profile(connection.provider).label + " | " + ConnectionStatusText(connection.status)
                + " | " + (connection.activeModels == null ? 0 : connection.activeModels.Count) + "/" + (connection.availableModels == null ? 0 : connection.availableModels.Count);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(row.x + 10f, row.y + 4f, row.width - 20f, 20f), title);
            GUI.color = new Color(0.74f, 0.78f, 0.82f, 1f);
            Widgets.Label(new Rect(row.x + 10f, row.y + 24f, row.width - 20f, 18f), subtitle);
            GUI.color = Color.white;

            if (Widgets.ButtonInvisible(row))
            {
                if (providerRemoveMode)
                {
                    ConfirmRemoveProvider(connection);
                }
                else
                {
                    selectedProviderId = connection.id;
                    providerDetailScrollPosition = Vector2.zero;
                }
            }
        }

        private void DrawProviderDetails(Rect rect)
        {
            OrcaLlmConnectionSettings connection = SelectedProvider();
            if (connection == null)
            {
                Widgets.Label(rect, "DTO_ApiKeyNoConnections".Translate());
                return;
            }

            List<string> filteredModels = FilteredAvailableModels(connection);
            float viewHeight = Mathf.Max(rect.height, 520f + filteredModels.Count * 30f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, viewHeight);
            Widgets.BeginScrollView(rect, ref providerDetailScrollPosition, viewRect);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            Text.Font = GameFont.Medium;
            listing.Label(connection.name + " (" + LlmProviderConfig.Profile(connection.provider).label + ")");
            Text.Font = GameFont.Small;
            listing.GapLine();

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
            listing.GapLine();
            listing.Label("DTO_ProviderActiveModels".Translate((connection.activeModels == null ? 0 : connection.activeModels.Count), (connection.availableModels == null ? 0 : connection.availableModels.Count)));
            listing.Label("DTO_ModelFilter".Translate());
            providerModelFilter = listing.TextEntry(providerModelFilter ?? "");
            DrawProviderModelRows(listing, connection, filteredModels);

            listing.End();
            Widgets.EndScrollView();
        }

        private void DrawProviderModelRows(Listing_Standard listing, OrcaLlmConnectionSettings connection, List<string> models)
        {
            if (connection.availableModels == null || connection.availableModels.Count == 0)
            {
                listing.Label("DTO_ModelNoDiscoveredModels".Translate());
                return;
            }

            if (models.Count == 0)
            {
                listing.Label("DTO_ModelNoMatchingModels".Translate());
                return;
            }

            for (int i = 0; i < models.Count; i++)
            {
                string modelId = models[i];
                bool active = connection.IsModelActive(modelId);
                Rect row = listing.GetRect(28f);
                if (active)
                {
                    Widgets.DrawBoxSolid(row, RowSelectedFill);
                }
                else if (Mouse.IsOver(row))
                {
                    Widgets.DrawBoxSolid(row, RowHoverFill);
                }

                Widgets.Label(new Rect(row.x + 8f, row.y + 4f, row.width - 16f, 22f), (active ? "[x] " : "[ ] ") + modelId);
                if (Widgets.ButtonInvisible(row))
                {
                    connection.SetModelActive(modelId, !active);
                }
            }
        }

        private void EnsureSelectedProvider()
        {
            if (Settings == null)
            {
                return;
            }

            Settings.EnsureLlmConnections();
            if (SelectedProvider() != null)
            {
                return;
            }

            selectedProviderId = Settings.llmConnections.Count == 0 || Settings.llmConnections[0] == null
                ? ""
                : Settings.llmConnections[0].id;
        }

        private OrcaLlmConnectionSettings SelectedProvider()
        {
            if (Settings == null || Settings.llmConnections == null)
            {
                return null;
            }

            for (int i = 0; i < Settings.llmConnections.Count; i++)
            {
                OrcaLlmConnectionSettings connection = Settings.llmConnections[i];
                if (connection != null && connection.id == selectedProviderId)
                {
                    return connection;
                }
            }

            return null;
        }

        private void ConfirmRemoveProvider(OrcaLlmConnectionSettings connection)
        {
            if (connection == null)
            {
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "DTO_ProviderDeleteConfirm".Translate(connection.name),
                delegate
                {
                    Settings.llmConnections.Remove(connection);
                    selectedProviderId = "";
                    EnsureSelectedProvider();
                },
                destructive: true));
        }

        private List<string> FilteredAvailableModels(OrcaLlmConnectionSettings connection)
        {
            if (connection == null || connection.availableModels == null)
            {
                return new List<string>();
            }

            string filter = providerModelFilter == null ? "" : providerModelFilter.Trim().ToLowerInvariant();
            List<string> result = new List<string>();
            for (int i = 0; i < connection.availableModels.Count; i++)
            {
                string model = connection.availableModels[i];
                if (model.NullOrEmpty())
                {
                    continue;
                }

                if (filter.NullOrEmpty() || model.ToLowerInvariant().Contains(filter))
                {
                    result.Add(model);
                }
            }

            return result;
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
            List<OrcaExtensionDescriptor> plugins = AllPluginDescriptors();
            if (plugins.Count == 0)
            {
                Widgets.Label(rect, "DTO_PluginManagerEmpty".Translate());
                return;
            }

            OrcaExtensionDescriptor selected = plugins.FirstOrDefault(p => p.id == selectedPluginId) ?? plugins[0];
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

        private void DrawPluginList(Rect rect, List<OrcaExtensionDescriptor> plugins)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), "DTO_PluginManagerInstalled".Translate());

            float y = rect.y + 32f;
            for (int i = 0; i < plugins.Count; i++)
            {
                OrcaExtensionDescriptor plugin = plugins[i];
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
                    pluginDetailScrollPosition = Vector2.zero;
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

        private void DrawPluginDetails(Rect rect, OrcaExtensionDescriptor plugin)
        {
            float viewWidth = Mathf.Max(10f, rect.width - 16f);
            float viewHeight = Mathf.Max(rect.height, PluginDetailContentHeight(plugin, viewWidth));
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
            Widgets.BeginScrollView(rect, ref pluginDetailScrollPosition, viewRect);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, viewWidth, 32f), plugin.Label);
            Text.Font = GameFont.Small;

            float y = 38f;
            Widgets.Label(new Rect(0f, y, viewWidth, 24f), "DTO_PluginCategory".Translate() + ": " + plugin.Category);
            y += 30f;
            Widgets.Label(new Rect(0f, y, viewWidth, 24f), PluginSourceText(plugin));
            y += 30f;

            bool enabled = PluginEnabled(plugin);
            bool oldEnabled = enabled;
            Rect checkboxRect = new Rect(0f, y, viewWidth, 28f);
            Widgets.CheckboxLabeled(checkboxRect, plugin.EnableLabel, ref enabled, false, null, null, false);
            TooltipHandler.TipRegion(checkboxRect, plugin.EnableTooltip);
            if (enabled != oldEnabled)
            {
                SetPluginEnabled(plugin, enabled);
            }
            y += 38f;

            Rect line = new Rect(0f, y, viewWidth, 1f);
            Widgets.DrawBoxSolid(line, BorderColor);
            y += 12f;

            float descriptionHeight = Mathf.Max(24f, Text.CalcHeight(plugin.Description, viewWidth));
            Widgets.Label(new Rect(0f, y, viewWidth, descriptionHeight), plugin.Description);
            y += descriptionHeight + 10f;
            float detailsHeight = Mathf.Max(48f, Text.CalcHeight(plugin.Details, viewWidth));
            Widgets.Label(new Rect(0f, y, viewWidth, detailsHeight), plugin.Details);
            y += detailsHeight + 14f;

            if (plugin.extensionDef != null && plugin.extensionDef.Worker != null)
            {
                plugin.extensionDef.Worker.DrawSettings(new Rect(0f, y, viewWidth, viewHeight - y));
            }

            Widgets.EndScrollView();
        }

        private static float PluginDetailContentHeight(OrcaExtensionDescriptor plugin, float width)
        {
            Text.Font = GameFont.Small;
            float height = 38f + 30f + 30f + 38f + 13f;
            height += Mathf.Max(24f, Text.CalcHeight(plugin.Description, width)) + 10f;
            height += Mathf.Max(48f, Text.CalcHeight(plugin.Details, width)) + 14f;
            if (plugin.extensionDef != null && plugin.extensionDef.Worker != null)
            {
                height += plugin.id == OrcaProactiveConversationManager.ExtensionDefName
                    ? RimtalkIntegration.IsAvailable ? 230f : 150f
                    : 180f;
            }

            return height + 20f;
        }

        private static List<OrcaExtensionDescriptor> AllPluginDescriptors()
        {
            List<OrcaExtensionDescriptor> result = new List<OrcaExtensionDescriptor>();
            List<OrcaExtensionDef> defs = OrcaExtensionManager.AllExtensionDefs();
            for (int i = 0; i < defs.Count; i++)
            {
                OrcaExtensionDef def = defs[i];
                if (def != null && !def.defName.NullOrEmpty())
                {
                    result.Add(new OrcaExtensionDescriptor(def));
                }
            }

            return result.OrderBy(plugin => plugin.Label).ToList();
        }

        private static bool PluginEnabled(OrcaExtensionDescriptor plugin)
        {
            if (plugin == null)
            {
                return false;
            }

            return Settings == null
                ? plugin.defaultEnabled
                : Settings.IsExtensionEnabled(plugin.id, plugin.defaultEnabled);
        }

        private static void SetPluginEnabled(OrcaExtensionDescriptor plugin, bool enabled)
        {
            if (plugin == null)
            {
                return;
            }

            if (Settings != null)
            {
                Settings.SetExtensionEnabled(plugin.id, enabled, plugin.defaultEnabled);
                if (plugin.extensionDef != null && plugin.extensionDef.Worker != null)
                {
                    if (enabled)
                    {
                        plugin.extensionDef.Worker.OnEnabled();
                    }
                    else
                    {
                        plugin.extensionDef.Worker.OnDisabled();
                    }
                }
                OrcaChatWindowManager.Session.Clear();
            }
        }

        public static string FormatEnabledPluginPrompt()
        {
            StringBuilder builder = new StringBuilder();
            OrcaExtensionManager.AppendSystemPrompt(builder);
            return builder.ToString().TrimEnd();
        }

        public static string FormatPluginControllerRoutingHint()
        {
            return OrcaExtensionManager.ControllerRoutingHint();
        }

        private static string SafeLine(string value)
        {
            return (value ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string PluginSourceText(OrcaExtensionDescriptor plugin)
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
