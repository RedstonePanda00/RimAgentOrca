using System.Collections.Generic;
using System.Linq;

namespace DeepseekTheOrca
{
    public sealed class ListAvailableIncidentsTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "list_available_incidents"; }
        }

        public string Description
        {
            get { return "List cached incidents that target the current map/world and can fire now."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            List<CachedIncidentDef> available = OrcaIncidentDefCache.AvailableFor(context).ToList();
            List<string> summaries = available.Select(incident => incident.Summary).ToList();

            return AiToolResult.Ok("available incident count: " + available.Count)
                .WithValue("incidentDefs", string.Join("; ", summaries.ToArray()));
        }
    }
}
