using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaSkillSettingsTab : OrcaSettingsTab
    {
        private Vector2 scrollPosition;

        public override string Id
        {
            get { return "skills"; }
        }

        public override string Label
        {
            get { return "DTO_SettingsPageSkills".Translate().ToString(); }
        }

        public override int Order
        {
            get { return 40; }
        }

        public override void OnSelected(OrcaSettingsContext context)
        {
            scrollPosition = Vector2.zero;
        }

        public override void Draw(Rect rect, OrcaSettingsContext context)
        {
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, 780f);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

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

            listing.End();
            Widgets.EndScrollView();
        }
    }
}
