namespace DeepseekTheOrca
{
    // Tools-layer view over the def tool enable/disable overrides persisted in
    // settings. Callers in the Tools layer should query/set through this class
    // instead of reaching into DeepseekTheOrcaSettings directly.
    public static class OrcaToolToggles
    {
        public static bool IsEnabled(OrcaToolDef def)
        {
            if (def == null)
            {
                return false;
            }

            return IsEnabled(def.defName, def.defaultEnabled);
        }

        public static bool IsEnabled(string defName, bool defaultEnabled)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            return settings == null ? defaultEnabled : settings.IsDefToolEnabled(defName, defaultEnabled);
        }

        public static void SetEnabled(OrcaToolDef def, bool enabled)
        {
            if (def == null)
            {
                return;
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings != null)
            {
                settings.SetDefToolEnabled(def.defName, enabled, def.defaultEnabled);
            }
        }
    }
}
