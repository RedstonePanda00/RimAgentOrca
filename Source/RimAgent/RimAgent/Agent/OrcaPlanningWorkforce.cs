using System.Text.Json.Nodes;
using RimAgent.Core;

namespace RimAgent;

// Workflows own rules and state. The selected workforce only produces a plan.
internal static class OrcaPlanningWorkforce
{
    public static bool Available => OrcaExternalBridge.IsControlled || RimAgentMod.Settings?.HasConfiguredLlm == true;
    public static IAiDecisionProvider Create(IncidentPlanningPolicy policy = null, string kind = "story_cycle", object input = null)
        => OrcaExternalBridge.IsControlled ? new ExternalIncidentDecisionProvider(policy, kind, input) : new LlmIncidentDecisionProvider(policy);
    public static void Cancel(IAiDecisionProvider planner) => (planner as IOrcaCancellableDecisionProvider)?.CancelPendingWork();
    public static object Request(IAiDecisionProvider planner, int mapId) => (planner as IExternalPlanningPort)?.ReadRequest(mapId);
    public static object Complete(IAiDecisionProvider planner, int mapId, string messageId, JsonObject plan)
        => planner is IExternalPlanningPort port ? port.CompleteRequest(mapId, messageId, plan)
            : throw new BridgeException("planning_unavailable", "No active external planning assignment.");
}

internal interface IExternalPlanningPort
{
    object ReadRequest(int mapId);
    object CompleteRequest(int mapId, string messageId, JsonObject plan);
}
