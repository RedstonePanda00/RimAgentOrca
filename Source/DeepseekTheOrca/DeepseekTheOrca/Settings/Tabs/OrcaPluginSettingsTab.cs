using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaPluginSettingsTab : OrcaSettingsTab
    {
        private Vector2 scrollPosition;

        public override string Id
        {
            get { return "plugins"; }
        }

        public override string Label
        {
            get { return "DTO_SettingsPagePlugins".Translate().ToString(); }
        }

        public override int Order
        {
            get { return 50; }
        }

        public override void OnSelected(OrcaSettingsContext context)
        {
            scrollPosition = Vector2.zero;
        }

        public override void Draw(Rect rect, OrcaSettingsContext context)
        {
            List<OrcaExtensionSettingsViewModel> plugins = AllPluginDescriptors();
            float viewHeight = Mathf.Max(rect.height, 90f + plugins.Count * 82f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, viewHeight);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);
            listing.Label("DTO_PluginManagerTitle".Translate());
            listing.Label("DTO_PluginManagerDescription".Translate());
            listing.Gap();
            DrawPluginList(listing, plugins, context);
            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawPluginList(Listing_Standard listing, List<OrcaExtensionSettingsViewModel> plugins, OrcaSettingsContext context)
        {
            if (plugins.Count == 0)
            {
                listing.Label("DTO_PluginManagerEmpty".Translate());
                return;
            }

            listing.Label("DTO_PluginManagerInstalled".Translate());
            for (int i = 0; i < plugins.Count; i++)
            {
                DrawPluginRow(listing.GetRect(76f), plugins[i], context);
                listing.Gap(6f);
            }
        }

        private static void DrawPluginRow(Rect row, OrcaExtensionSettingsViewModel plugin, OrcaSettingsContext context)
        {
            if (Mouse.IsOver(row))
            {
                Widgets.DrawBoxSolid(row, OrcaSettingsWidgets.RowHoverFill);
            }

            bool oldEnabled = PluginEnabled(plugin, context);
            bool enabled = oldEnabled;
            Rect checkboxRect = new Rect(row.x + 8f, row.y + 8f, 28f, 28f);
            Widgets.Checkbox(checkboxRect.x, checkboxRect.y, ref enabled);
            TooltipHandler.TipRegion(checkboxRect, plugin.EnableTooltip);
            if (enabled != oldEnabled)
            {
                SetPluginEnabled(plugin, enabled, context);
            }

            Rect settingsRect = new Rect(row.xMax - 86f, row.y + 9f, 76f, 32f);
            if (Widgets.ButtonText(settingsRect, "DTO_PluginSettingsButton".Translate()))
            {
                Find.WindowStack.Add(new OrcaPluginSettingsWindow(plugin, context));
            }
            TooltipHandler.TipRegion(settingsRect, "DTO_PluginSettingsButtonTooltip".Translate());

            float textX = checkboxRect.xMax + 8f;
            float textWidth = settingsRect.x - textX - 8f;
            Widgets.Label(new Rect(textX, row.y + 5f, textWidth, 24f), plugin.Label);
            string subtitle = PluginMetadataText(plugin, context);
            GUI.color = new Color(0.74f, 0.78f, 0.82f, 1f);
            Widgets.Label(new Rect(textX, row.y + 29f, textWidth, 20f), subtitle);
            GUI.color = Color.white;
            Widgets.Label(new Rect(textX, row.y + 50f, textWidth, 20f), plugin.Description);
            TooltipHandler.TipRegion(row, plugin.Details.NullOrEmpty() ? plugin.Description : plugin.Details);
        }

        private static List<OrcaExtensionSettingsViewModel> AllPluginDescriptors()
        {
            List<OrcaExtensionSettingsViewModel> result = new List<OrcaExtensionSettingsViewModel>();
            List<OrcaExtensionDef> defs = OrcaExtensionManager.AllExtensionDefs();
            for (int i = 0; i < defs.Count; i++)
            {
                OrcaExtensionDef def = defs[i];
                if (def != null && !def.defName.NullOrEmpty())
                {
                    result.Add(new OrcaExtensionSettingsViewModel(def));
                }
            }

            return result.OrderBy(plugin => plugin.Label).ToList();
        }

        private static bool PluginEnabled(OrcaExtensionSettingsViewModel plugin, OrcaSettingsContext context)
        {
            if (plugin == null)
            {
                return false;
            }

            DeepseekTheOrcaSettings settings = context == null ? null : context.settings;
            return settings == null
                ? plugin.defaultEnabled
                : settings.IsExtensionEnabled(plugin.id, plugin.defaultEnabled);
        }

        private static void SetPluginEnabled(OrcaExtensionSettingsViewModel plugin, bool enabled, OrcaSettingsContext context)
        {
            DeepseekTheOrcaSettings settings = context == null ? null : context.settings;
            if (plugin == null || settings == null)
            {
                return;
            }

            OrcaExtensionManager.SetExtensionEnabled(plugin.extensionDef, enabled);

            OrcaChatWindowManager.Session.Clear();
            if (context != null)
            {
                context.WriteSettings();
            }
        }

        private static string PluginMetadataText(OrcaExtensionSettingsViewModel plugin, OrcaSettingsContext context)
        {
            string source = plugin == null ? "" : plugin.sourceMod;
            if (source.NullOrEmpty())
            {
                source = "DTO_ExtensionSourceLocal".Translate().ToString();
            }

            string state = PluginEnabled(plugin, context) ? "DTO_PluginStateEnabled".Translate().ToString() : "DTO_PluginStateDisabled".Translate().ToString();
            string author = plugin == null ? "" : plugin.Author;
            string authorText = author.NullOrEmpty() ? "" : " | " + "DTO_PluginAuthor".Translate().ToString() + ": " + author;
            string capabilities = plugin == null || plugin.Capabilities.NullOrEmpty() ? "" : " | Capabilities: " + plugin.Capabilities;
            return state + " | " + "DTO_PluginCategory".Translate().ToString() + ": " + plugin.Category + " | " + "DTO_ExtensionSource".Translate().ToString() + ": " + source + authorText + capabilities;
        }
    }
}
