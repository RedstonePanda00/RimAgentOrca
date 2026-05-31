using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaPluginSettingsWindow : Window
    {
        private readonly OrcaExtensionSettingsViewModel plugin;
        private readonly OrcaSettingsContext context;

        public OrcaPluginSettingsWindow(OrcaExtensionSettingsViewModel plugin, OrcaSettingsContext context)
        {
            this.plugin = plugin;
            this.context = context;
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize
        {
            get
            {
                if (plugin != null && plugin.extensionDef != null && plugin.extensionDef.SettingsWorker != null)
                {
                    return plugin.extensionDef.SettingsWorker.WindowSize;
                }

                return new Vector2(700f, 520f);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (plugin == null)
            {
                Close();
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), plugin.Label);
            Text.Font = GameFont.Small;

            float y = inRect.y + 38f;
            if (!plugin.Author.NullOrEmpty())
            {
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "DTO_PluginAuthor".Translate() + ": " + plugin.Author);
                y += 28f;
            }

            if (!plugin.Capabilities.NullOrEmpty())
            {
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "Capabilities: " + plugin.Capabilities);
                y += 28f;
            }

            if (!plugin.Permissions.NullOrEmpty())
            {
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "Permissions: " + plugin.Permissions);
                y += 28f;
            }

            float descriptionHeight = Mathf.Max(24f, Text.CalcHeight(plugin.Description, inRect.width));
            Widgets.Label(new Rect(inRect.x, y, inRect.width, descriptionHeight), plugin.Description);
            y += descriptionHeight + 10f;

            Rect line = new Rect(inRect.x, y, inRect.width, 1f);
            Widgets.DrawBoxSolid(line, OrcaSettingsWidgets.BorderColor);
            y += 12f;

            Rect settingsRect = new Rect(inRect.x, y, inRect.width, inRect.height - (y - inRect.y));
            bool drewSettings = false;
            if (OrcaExtensionSettingsDrawer.HasSchema(plugin.extensionDef))
            {
                OrcaExtensionSettingsDrawer.DrawSchema(settingsRect, plugin.extensionDef, context);
                drewSettings = true;
            }
            else if (plugin.extensionDef != null && plugin.extensionDef.SettingsWorker != null)
            {
                plugin.extensionDef.SettingsWorker.DrawSettings(settingsRect, context);
                drewSettings = true;
            }

            if (!drewSettings)
            {
                Widgets.Label(settingsRect, "DTO_PluginNoSettings".Translate());
            }
        }
    }
}
