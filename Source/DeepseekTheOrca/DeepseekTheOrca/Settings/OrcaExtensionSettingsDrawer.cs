using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaExtensionSettingsDrawer
    {
        private static readonly Dictionary<string, string> buffers = new Dictionary<string, string>();

        public static bool HasSchema(OrcaExtensionDef def)
        {
            return def != null && def.settings != null && def.settings.Count > 0;
        }

        public static void DrawSchema(Rect rect, OrcaExtensionDef def, OrcaSettingsContext context)
        {
            if (!HasSchema(def))
            {
                return;
            }

            DeepseekTheOrcaSettings settings = context == null ? null : context.settings;
            if (settings == null)
            {
                Widgets.Label(rect, "DTO_PluginNoSettings".Translate());
                return;
            }

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);
            for (int i = 0; i < def.settings.Count; i++)
            {
                DrawEntry(listing, def, def.settings[i], settings, context);
            }
            listing.End();
        }

        private static void DrawEntry(Listing_Standard listing, OrcaExtensionDef def, OrcaExtensionSettingEntry entry, DeepseekTheOrcaSettings settings, OrcaSettingsContext context)
        {
            if (entry == null)
            {
                return;
            }

            FieldInfo field = typeof(DeepseekTheOrcaSettings).GetField(entry.fieldName ?? "");
            if (field == null)
            {
                listing.Label("Missing setting field: " + (entry.fieldName ?? ""));
                return;
            }

            string label = Text(entry.label.NullOrEmpty() ? entry.key : entry.label);
            string tooltip = Text(entry.tooltip);
            object before = field.GetValue(settings);
            string type = (entry.type ?? "").ToLowerInvariant();
            if (type == "bool" && field.FieldType == typeof(bool))
            {
                bool value = (bool)before;
                listing.CheckboxLabeled(label, ref value, tooltip.NullOrEmpty() ? null : tooltip);
                if (value != (bool)before)
                {
                    field.SetValue(settings, value);
                    OnChanged(entry, context);
                }
                return;
            }

            if (type == "int" && field.FieldType == typeof(int))
            {
                int value = (int)before;
                string buffer = Buffer(def, entry, value.ToString());
                listing.TextFieldNumericLabeled(label, ref value, ref buffer, (int)entry.min, (int)entry.max);
                SetBuffer(def, entry, buffer);
                if (value != (int)before)
                {
                    field.SetValue(settings, value);
                    OnChanged(entry, context);
                }
                return;
            }

            if ((type == "float" || type == "percent") && field.FieldType == typeof(float))
            {
                float value = (float)before;
                string buffer = Buffer(def, entry, value.ToString("0.###"));
                listing.TextFieldNumericLabeled(label, ref value, ref buffer, entry.min, entry.max);
                SetBuffer(def, entry, buffer);
                if (!Mathf.Approximately(value, (float)before))
                {
                    field.SetValue(settings, value);
                    OnChanged(entry, context);
                }
                return;
            }

            if (type == "string" && field.FieldType == typeof(string))
            {
                listing.Label(label);
                string value = (string)before ?? "";
                string next = listing.TextEntry(value);
                if (next != value)
                {
                    field.SetValue(settings, next);
                    OnChanged(entry, context);
                }
                return;
            }

            listing.Label("Unsupported setting schema: " + label + " (" + entry.type + " -> " + field.FieldType.Name + ")");
        }

        private static void OnChanged(OrcaExtensionSettingEntry entry, OrcaSettingsContext context)
        {
            if (entry == null || entry.clearChatOnChange)
            {
                OrcaChatWindowManager.Session.Clear();
            }

            if (context != null)
            {
                context.WriteSettings();
            }
        }

        private static string Buffer(OrcaExtensionDef def, OrcaExtensionSettingEntry entry, string fallback)
        {
            string key = BufferKey(def, entry);
            string value;
            if (buffers.TryGetValue(key, out value))
            {
                return value;
            }

            buffers[key] = fallback ?? "";
            return buffers[key];
        }

        private static void SetBuffer(OrcaExtensionDef def, OrcaExtensionSettingEntry entry, string value)
        {
            buffers[BufferKey(def, entry)] = value ?? "";
        }

        private static string BufferKey(OrcaExtensionDef def, OrcaExtensionSettingEntry entry)
        {
            return (def == null ? "" : def.defName) + ":" + (entry == null ? "" : entry.key);
        }

        private static string Text(string value)
        {
            if (value.NullOrEmpty())
            {
                return "";
            }

            return value.StartsWith("DTO_") ? value.Translate().ToString() : value;
        }
    }
}
