using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class CanFireIncidentTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "can_fire_incident"; }
        }

        public string Description
        {
            get { return "Validate one cached incident def against the current target and storyteller settings."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            string defName;
            if (!arguments.TryGetValue("incidentDef", out defName) || defName.NullOrEmpty())
            {
                return AiToolResult.Fail("missing argument: incidentDef");
            }

            float pointsFactor = 1f;
            string pointsFactorText;
            if (arguments.TryGetValue("pointsFactor", out pointsFactorText))
            {
                float.TryParse(pointsFactorText, out pointsFactor);
            }

            AiIncidentPlan plan = AiIncidentPlan.For(defName, "validation only", pointsFactor);
            FiringIncident ignored;
            string reason;
            if (OrcaIncidentValidator.TryBuildFiringIncident(plan, context, out ignored, out reason))
            {
                return AiToolResult.Ok(defName + " can fire");
            }

            return AiToolResult.Fail(reason);
        }
    }
}
