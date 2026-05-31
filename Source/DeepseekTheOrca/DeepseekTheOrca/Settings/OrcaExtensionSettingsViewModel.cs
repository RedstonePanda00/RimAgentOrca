using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaExtensionSettingsViewModel
    {
        public readonly string id;
        public readonly bool defaultEnabled;
        public readonly string sourceMod;
        public readonly OrcaExtensionDef extensionDef;
        public readonly string capabilitiesText;
        public readonly string permissionsText;
        private readonly string labelText;
        private readonly string categoryText;
        private readonly string descriptionText;
        private readonly string detailsText;
        private readonly string authorText;
        private readonly string enableLabelText;
        private readonly string enableTooltipText;

        public OrcaExtensionSettingsViewModel(OrcaExtensionDef def)
        {
            id = def.defName;
            labelText = def.label.NullOrEmpty() ? def.defName : def.label;
            categoryText = def.category.NullOrEmpty() ? "Extension" : def.category;
            descriptionText = def.description ?? "";
            detailsText = def.details ?? "";
            authorText = def.author ?? "";
            enableLabelText = "";
            enableTooltipText = descriptionText;
            defaultEnabled = def.defaultEnabled;
            sourceMod = IsCoreMod(def.modContentPack) ? "Core" : def.modContentPack == null ? "" : def.modContentPack.Name;
            extensionDef = def;
            capabilitiesText = TextList(def.capabilities);
            permissionsText = TextList(def.permissions);
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

        public string Author
        {
            get { return Text(authorText); }
        }

        public string EnableLabel
        {
            get { return enableLabelText.NullOrEmpty() ? "DTO_EnableExtension".Translate(Label).ToString() : Text(enableLabelText); }
        }

        public string EnableTooltip
        {
            get { return Text(enableTooltipText); }
        }

        public string Capabilities
        {
            get { return capabilitiesText; }
        }

        public string Permissions
        {
            get { return permissionsText; }
        }

        private static string Text(string value)
        {
            if (value.NullOrEmpty())
            {
                return "";
            }

            return value.StartsWith("DTO_") ? value.Translate().ToString() : value;
        }

        private static string TextList(System.Collections.Generic.List<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "";
            }

            System.Collections.Generic.List<string> labels = new System.Collections.Generic.List<string>();
            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                if (!value.NullOrEmpty())
                {
                    labels.Add(Text(value));
                }
            }

            return string.Join(", ", labels.ToArray());
        }

        private static bool IsCoreMod(ModContentPack mod)
        {
            return mod != null
                && DeepseekTheOrcaMod.Instance != null
                && DeepseekTheOrcaMod.Instance.Content != null
                && mod.PackageId == DeepseekTheOrcaMod.Instance.Content.PackageId;
        }
    }
}
