using System.Collections.Generic;
using RimWorld;

namespace DeepseekTheOrca
{
    public sealed class TriggerRaidTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "trigger_raid"; }
        }

        public string Description
        {
            get { return "Validate a precise enemy raid request with optional faction, raid strategy, arrival mode, and spawn cell. Execution is owned by the caller."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            IncidentParms parms;
            string rejectReason;
            if (!OrcaRaidUtility.TryBuildRaidParms(context, arguments, out parms, out rejectReason))
            {
                return AiToolResult.Fail(rejectReason);
            }

            return AiToolResult.Ok("raid validated for triggering")
                .WithValue("incidentDef", "RaidEnemy")
                .WithValue("faction", parms.faction == null ? "" : parms.faction.def.defName)
                .WithValue("raidStrategy", parms.raidStrategy == null ? "" : parms.raidStrategy.defName)
                .WithValue("raidArrivalMode", parms.raidArrivalMode == null ? "" : parms.raidArrivalMode.defName)
                .WithValue("spawnCenter", parms.spawnCenter.IsValid ? parms.spawnCenter.ToString() : "")
                .WithValue("points", parms.points.ToString("F0"));
        }
    }
}
