using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class ScheduleIncidentTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "schedule_incident"; }
        }

        public string Description
        {
            get { return "Validate an incident proposal for storyteller execution. The comp still owns the actual firing."; }
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

            string reason;
            arguments.TryGetValue("reason", out reason);

            AiIncidentPlan plan = AiIncidentPlan.For(defName, reason ?? "AI storyteller selected this incident.", pointsFactor);
            FiringIncident ignored;
            string rejectReason;
            if (!OrcaIncidentValidator.TryBuildFiringIncident(plan, context, out ignored, out rejectReason))
            {
                return AiToolResult.Fail(rejectReason);
            }

            return AiToolResult.Ok("incident validated for scheduling")
                .WithValue("incidentDef", defName)
                .WithValue("pointsFactor", pointsFactor.ToString("F2"))
                .WithValue("reason", reason ?? "");
        }
    }
}
