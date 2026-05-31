namespace DeepseekTheOrca
{
    public sealed class OrcaSettingsContext
    {
        public readonly DeepseekTheOrcaMod mod;
        public readonly DeepseekTheOrcaSettings settings;

        public OrcaSettingsContext(DeepseekTheOrcaMod mod, DeepseekTheOrcaSettings settings)
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
