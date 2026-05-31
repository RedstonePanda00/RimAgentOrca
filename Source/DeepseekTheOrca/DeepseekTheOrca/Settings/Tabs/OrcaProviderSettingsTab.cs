using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaProviderSettingsTab : OrcaSettingsTab
    {
        private Vector2 providerListScrollPosition;
        private Vector2 providerDetailScrollPosition;
        private string selectedProviderId = "";
        private string providerModelFilter = "";
        private bool providerRemoveMode;

        public override string Id
        {
            get { return "providers"; }
        }

        public override string Label
        {
            get { return "DTO_SettingsPageProviders".Translate().ToString(); }
        }

        public override int Order
        {
            get { return 10; }
        }

        public override void OnSelected(OrcaSettingsContext context)
        {
            providerDetailScrollPosition = Vector2.zero;
        }

        public override void Draw(Rect rect, OrcaSettingsContext context)
        {
            DeepseekTheOrcaSettings settings = context == null ? null : context.settings;
            if (settings == null)
            {
                return;
            }

            settings.EnsureLlmConnections();
            EnsureSelectedProvider(settings);

            float listWidth = Mathf.Min(260f, Mathf.Max(210f, rect.width * 0.34f));
            Rect listRect = new Rect(rect.x, rect.y, listWidth, rect.height);
            Rect detailRect = new Rect(listRect.xMax + 10f, rect.y, rect.width - listWidth - 10f, rect.height);

            OrcaSettingsWidgets.DrawPanel(listRect);
            OrcaSettingsWidgets.DrawPanel(detailRect);
            DrawProviderList(listRect.ContractedBy(8f), settings);
            DrawProviderDetails(detailRect.ContractedBy(10f), settings);
        }

        private void DrawProviderList(Rect rect, DeepseekTheOrcaSettings settings)
        {
            settings.EnsureLlmConnections();

            Rect toolbar = new Rect(rect.x, rect.y, rect.width, 32f);
            Rect addRect = new Rect(toolbar.x, toolbar.y, 34f, 30f);
            Rect removeModeRect = new Rect(addRect.xMax + 6f, toolbar.y, 34f, 30f);
            if (Widgets.ButtonText(addRect, "+"))
            {
                OrcaLlmConnectionSettings connection = settings.AddLlmConnection();
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

            if (settings.llmConnections.Count == 0)
            {
                Widgets.Label(new Rect(rect.x, toolbar.yMax + 8f, rect.width, 40f), "DTO_ApiKeyNoConnections".Translate());
                return;
            }

            float rowHeight = 46f;
            Rect outRect = new Rect(rect.x, toolbar.yMax + 8f, rect.width, rect.height - toolbar.height - 8f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, settings.llmConnections.Count * (rowHeight + 6f));
            Widgets.BeginScrollView(outRect, ref providerListScrollPosition, viewRect);
            for (int i = 0; i < settings.llmConnections.Count; i++)
            {
                OrcaLlmConnectionSettings connection = settings.llmConnections[i];
                if (connection == null)
                {
                    continue;
                }

                Rect row = new Rect(0f, i * (rowHeight + 6f), viewRect.width, rowHeight);
                DrawProviderListRow(row, connection, settings);
            }
            Widgets.EndScrollView();
        }

        private void DrawProviderListRow(Rect row, OrcaLlmConnectionSettings connection, DeepseekTheOrcaSettings settings)
        {
            bool selected = selectedProviderId == connection.id;
            bool hover = Mouse.IsOver(row);
            if (selected)
            {
                Widgets.DrawBoxSolid(row, OrcaSettingsWidgets.RowSelectedFill);
                Widgets.DrawBoxSolid(new Rect(row.x, row.y, 3f, row.height), new Color(0.45f, 0.68f, 0.88f, 1f));
            }
            else if (hover)
            {
                Widgets.DrawBoxSolid(row, OrcaSettingsWidgets.RowHoverFill);
            }

            string title = providerRemoveMode ? "[-] " + connection.name : connection.name;
            string subtitle = LlmProviderConfig.Profile(connection.provider).label + " | " + OrcaSettingsFormatters.ConnectionStatusText(connection.status)
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
                    ConfirmRemoveProvider(connection, settings);
                }
                else
                {
                    selectedProviderId = connection.id;
                    providerDetailScrollPosition = Vector2.zero;
                }
            }
        }

        private void DrawProviderDetails(Rect rect, DeepseekTheOrcaSettings settings)
        {
            OrcaLlmConnectionSettings connection = SelectedProvider(settings);
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

            listing.Label("DTO_ConnectionStatus".Translate() + ": " + OrcaSettingsFormatters.ConnectionStatusText(connection.status));
            listing.Label(OrcaSettingsFormatters.TranslateIfKey(connection.message));
            listing.GapLine();
            listing.Label("DTO_ProviderActiveModels".Translate((connection.activeModels == null ? 0 : connection.activeModels.Count), (connection.availableModels == null ? 0 : connection.availableModels.Count)));
            listing.Label("DTO_ModelFilter".Translate());
            providerModelFilter = listing.TextEntry(providerModelFilter ?? "");
            DrawProviderModelRows(listing, connection, filteredModels);

            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawProviderModelRows(Listing_Standard listing, OrcaLlmConnectionSettings connection, List<string> models)
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
                    Widgets.DrawBoxSolid(row, OrcaSettingsWidgets.RowSelectedFill);
                }
                else if (Mouse.IsOver(row))
                {
                    Widgets.DrawBoxSolid(row, OrcaSettingsWidgets.RowHoverFill);
                }

                Widgets.Label(new Rect(row.x + 8f, row.y + 4f, row.width - 16f, 22f), (active ? "[x] " : "[ ] ") + modelId);
                if (Widgets.ButtonInvisible(row))
                {
                    connection.SetModelActive(modelId, !active);
                }
            }
        }

        private void EnsureSelectedProvider(DeepseekTheOrcaSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.EnsureLlmConnections();
            if (SelectedProvider(settings) != null)
            {
                return;
            }

            selectedProviderId = settings.llmConnections.Count == 0 || settings.llmConnections[0] == null
                ? ""
                : settings.llmConnections[0].id;
        }

        private OrcaLlmConnectionSettings SelectedProvider(DeepseekTheOrcaSettings settings)
        {
            if (settings == null || settings.llmConnections == null)
            {
                return null;
            }

            for (int i = 0; i < settings.llmConnections.Count; i++)
            {
                OrcaLlmConnectionSettings connection = settings.llmConnections[i];
                if (connection != null && connection.id == selectedProviderId)
                {
                    return connection;
                }
            }

            return null;
        }

        private void ConfirmRemoveProvider(OrcaLlmConnectionSettings connection, DeepseekTheOrcaSettings settings)
        {
            if (connection == null || settings == null)
            {
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "DTO_ProviderDeleteConfirm".Translate(connection.name),
                delegate
                {
                    settings.llmConnections.Remove(connection);
                    selectedProviderId = "";
                    EnsureSelectedProvider(settings);
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

    }
}
