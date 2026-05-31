using System.Collections.Generic;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class GetColonySummaryTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "get_colony_summary"; }
        }

        public string Description
        {
            get { return "Read a compact storyteller-safe summary of the current incident target."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            ColonySnapshot snapshot = ColonySnapshot.Capture(context.target);
            return AiToolResult.Ok("colony summary captured")
                .WithValue("colonists", snapshot.colonists)
                .WithValue("downedColonists", snapshot.downedColonists)
                .WithValue("mentalStateColonists", snapshot.mentalStateColonists)
                .WithValue("averageMood", snapshot.averageMood.ToStringPercent())
                .WithValue("playerWealth", snapshot.playerWealth.ToString("F0"))
                .WithValue("threatPoints", snapshot.threatPoints.ToString("F0"))
                .WithValue("humanEdibleNutrition", snapshot.humanEdibleNutrition.ToString("F1"))
                .WithValue("recentIncidents", snapshot.recentIncidents);
        }
    }
}
