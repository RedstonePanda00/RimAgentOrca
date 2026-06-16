using System.Collections.Generic;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaStorytellerUtility
    {
        public static bool IsActiveOrcaStoryteller
        {
            get
            {
                return Find.Storyteller != null
                    && Find.Storyteller.def != null
                    && Find.Storyteller.def.defName == "DTO_DeepseekTheOrca";
            }
        }

        public static StorytellerComp_DeepseekOrca ActiveOrcaComp()
        {
            if (Find.Storyteller == null || Find.Storyteller.storytellerComps == null)
            {
                return null;
            }

            for (int i = 0; i < Find.Storyteller.storytellerComps.Count; i++)
            {
                StorytellerComp_DeepseekOrca comp = Find.Storyteller.storytellerComps[i] as StorytellerComp_DeepseekOrca;
                if (comp != null)
                {
                    return comp;
                }
            }

            return null;
        }

        public static bool TryBuildIncidentPlan(
            Dictionary<string, string> arguments,
            string defaultReason,
            out AiIncidentPlan plan,
            out string rejectReason)
        {
            plan = null;
            rejectReason = null;

            string incidentDef;
            if (arguments == null || !arguments.TryGetValue("incidentDef", out incidentDef) || incidentDef.NullOrEmpty())
            {
                rejectReason = "missing argument: incidentDef";
                return false;
            }

            float pointsFactor = 1f;
            string pointsFactorText;
            if (arguments.TryGetValue("pointsFactor", out pointsFactorText))
            {
                float.TryParse(pointsFactorText, out pointsFactor);
            }

            string reason;
            arguments.TryGetValue("reason", out reason);
            plan = AiIncidentPlan.For(incidentDef, reason ?? defaultReason, pointsFactor);
            return true;
        }
    }
}
