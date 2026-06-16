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

        public override AiToolResult ExecuteValidated(AiToolContext context, Dictionary<string, string> arguments, List<string> processLines)
        {
            StorytellerComp_DeepseekOrca comp = OrcaStorytellerUtility.ActiveOrcaComp();
            if (comp == null)
            {
                return AiToolResult.Fail("active storyteller does not contain StorytellerComp_DeepseekOrca");
            }

            AiIncidentPlan plan;
            string rejectReason;
            if (!OrcaStorytellerUtility.TryBuildIncidentPlan(arguments, "The chat agent selected this incident.", out plan, out rejectReason))
            {
                return AiToolResult.Fail(rejectReason);
            }

            string message;
            string traceText;
            bool fired = comp.TryFireIncidentNowForDebug(context == null ? null : context.target, plan, out message, out traceText);
            if (!fired)
            {
                return AiToolResult.Fail(message);
            }

            return AiToolResult.Ok(message)
                .WithValue("incidentDef", plan.incidentDefName)
                .WithValue("reason", plan.reason ?? "");
        }
    }
}
