using System.Collections.Generic;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class ProposeIncidentTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "propose_incident"; }
        }

        public string Description
        {
            get { return "Create a structured incident proposal. This does not execute anything."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            string defName;
            if (!arguments.TryGetValue("incidentDef", out defName) || defName.NullOrEmpty())
            {
                return AiToolResult.Fail("missing argument: incidentDef");
            }

            string reason;
            arguments.TryGetValue("reason", out reason);

            float pointsFactor = 1f;
            string pointsFactorText;
            if (arguments.TryGetValue("pointsFactor", out pointsFactorText))
            {
                float.TryParse(pointsFactorText, out pointsFactor);
            }

            return AiToolResult.Ok("incident proposal created")
                .WithValue("incidentDef", defName)
                .WithValue("pointsFactor", pointsFactor.ToString("F2"))
                .WithValue("reason", reason ?? "");
        }
    }
}
