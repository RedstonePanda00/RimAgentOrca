using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaIncidentValidator
    {
        public static bool TryBuildFiringIncident(AiIncidentPlan plan, AiToolContext context, out FiringIncident firingIncident, out string rejectReason)
        {
            firingIncident = null;
            rejectReason = null;

            if (plan == null || plan.incidentDefName.NullOrEmpty())
            {
                rejectReason = "missing incident def";
                return false;
            }

            CachedIncidentDef cached;
            if (!OrcaIncidentDefCache.TryGet(plan.incidentDefName, out cached))
            {
                rejectReason = "incident is not in Orca incident cache: " + plan.incidentDefName;
                return false;
            }

            IncidentDef incidentDef = cached.Def;
            if (!incidentDef.TargetAllowed(context.target))
            {
                rejectReason = "incident target is not allowed: " + incidentDef.defName;
                return false;
            }

            IncidentParms parms = StorytellerUtility.DefaultParmsNow(incidentDef.category, context.target);
            if (parms == null)
            {
                rejectReason = "could not create incident parms";
                return false;
            }

            if (parms.points >= 0f)
            {
                float min = context.props == null ? 0.75f : context.props.pointsFactorRange.min;
                float max = context.props == null ? 1.25f : context.props.pointsFactorRange.max;
                float factor = Mathf.Clamp(plan.pointsFactor <= 0f ? 1f : plan.pointsFactor, min, max);
                parms.points *= factor;
            }

            if (!plan.customLetterLabel.NullOrEmpty())
            {
                parms.customLetterLabel = plan.customLetterLabel;
            }

            if (!plan.customLetterText.NullOrEmpty())
            {
                parms.customLetterText = plan.customLetterText;
            }

            if (!incidentDef.Worker.CanFireNow(parms))
            {
                rejectReason = "IncidentWorker.CanFireNow returned false for " + incidentDef.defName;
                return false;
            }

            firingIncident = new FiringIncident(incidentDef, context.source, parms);
            return true;
        }
    }
}
