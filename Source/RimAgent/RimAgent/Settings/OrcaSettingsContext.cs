namespace RimAgent
{
    public sealed class OrcaSettingsContext
    {
        public readonly RimAgentMod mod;
        public readonly RimAgentSettings settings;

        public OrcaSettingsContext(RimAgentMod mod, RimAgentSettings settings)
        {
            this.mod = mod;
            this.settings = settings;
        }

        public void WriteSettings()
        {
            if (mod != null)
            {
                mod.WriteSettings();
            }
        }
    }
}
