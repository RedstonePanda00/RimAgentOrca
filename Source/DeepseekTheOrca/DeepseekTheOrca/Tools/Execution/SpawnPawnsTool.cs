using System.Collections.Generic;

namespace DeepseekTheOrca
{
    public sealed class SpawnPawnsTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "spawn_pawns"; }
        }

        public string Description
        {
            get { return "Validate spawning a number of default faction pawns near a specified map cell. Execution is owned by the caller."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            PawnSpawnRequest request;
            string rejectReason;
            if (!OrcaPawnSpawnUtility.TryBuildRequest(context, arguments, out request, out rejectReason))
            {
                return AiToolResult.Fail(rejectReason);
            }

            return AiToolResult.Ok("pawn spawn validated")
                .WithValue("faction", request.faction.def.defName)
                .WithValue("count", request.count)
                .WithValue("spawnCell", request.spawnCell)
                .WithValue("radius", request.radius);
        }

        public override AiToolResult ExecuteValidated(AiToolContext context, Dictionary<string, string> arguments, List<string> processLines)
        {
            string message;
            bool spawned = OrcaPawnSpawnUtility.TrySpawnPawns(context, arguments, out message);
            if (!spawned)
            {
                return AiToolResult.Fail(message);
            }

            return AiToolResult.Ok(message);
        }
    }
}
