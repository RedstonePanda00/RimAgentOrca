using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaPersonaSettingsTab : OrcaSettingsTab
    {
        private Vector2 scrollPosition;

        public override string Id
        {
            get { return "personas"; }
        }

        public override string Label
        {
            get { return "DTO_SettingsPagePersonas".Translate().ToString(); }
        }

        public override int Order
        {
            get { return 30; }
        }

        public override void OnSelected(OrcaSettingsContext context)
        {
            scrollPosition = Vector2.zero;
        }

        public override void Draw(Rect rect, OrcaSettingsContext context)
        {
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, 520f);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            DrawPersonaSelector(listing, context);
            listing.Gap();
            listing.Label("DTO_ChatPersonaFolder".Translate() + ": " + OrcaChatPersonaManager.PersonaFolderPath);
            listing.Label("DTO_ChatPersonaFormatNote".Translate());

            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawPersonaSelector(Listing_Standard listing, OrcaSettingsContext context)
        {
            DeepseekTheOrcaSettings settings = context == null ? null : context.settings;
            if (settings == null)
            {
                return;
            }

            listing.Label("DTO_ChatPersona".Translate(), -1f, "DTO_ChatPersonaTooltip".Translate());
            OrcaChatPersonaProfile selected = OrcaChatPersonaManager.Get(settings.chatPersonaDefName);
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
    }
}
