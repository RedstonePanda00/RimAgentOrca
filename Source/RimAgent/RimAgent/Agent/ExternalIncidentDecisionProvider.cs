using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using RimAgent.Core;
using Verse;

namespace RimAgent;

internal sealed class ExternalIncidentDecisionProvider(IncidentPlanningPolicy suppliedPolicy = null, string kind = "story_cycle", object input = null) : IAiDecisionProvider, IOrcaCancellableDecisionProvider, IExternalPlanningPort
{
    private RelayRequest pending;
    private string status = "External narrator ready.";
    private DateTime retryAfter;
    private int requestMap;
    private float requestDays;
    private int requestBudget;
    private IncidentPlanningPolicy requestPolicy;
    public bool HasPendingWork => pending != null;
    public string LastStatus => status;
    public IEnumerable<string> LogLines => new[] { status };
    public void CancelPendingWork() { pending?.Cancel(); pending = null; }
    public object ReadRequest(int mapId)
    {
        if (pending != null && mapId != requestMap) throw new BridgeException("map_forbidden", "Planning request belongs to another map.");
        return pending?.View();
    }
    public object CompleteRequest(int mapId, string messageId, JsonObject plan)
    {
        ReadRequest(mapId);
        if (pending == null || pending.Id != messageId || plan == null || !pending.TryComplete(new JsonObject { ["plan"] = plan.DeepClone() }))
            throw new BridgeException("request_closed", "Unknown or closed planning request.");
        return new { accepted = true, messageId };
    }
    internal OrcaIncidentCyclePlan ConsumeCompleted(out AiToolContext context, out float days)
    {
        context = null; days = requestDays;
        if (pending == null || !pending.Result.IsCompleted) return null;
        try
        {
            context = OrcaAgentGameAdapter.StoryContext(requestMap);
            return SelectIncidentCyclePlan(context, requestDays, requestBudget);
        }
        catch (Exception ex) { CancelPendingWork(); status = ex.Message; return null; }
    }
    public OrcaIncidentCyclePlan SelectIncidentCyclePlan(AiToolContext context, float cycleDays, int cycleBudget)
    {
        if (DateTime.UtcNow < retryAfter) return null;
        if (pending == null)
        {
            try
            {
                var policy = suppliedPolicy ?? OrcaPersonaBehaviors.Current.CreatePlanningPolicy(context, cycleDays, cycleBudget);
                requestPolicy = policy;
                requestMap = context.Map.uniqueID; requestDays = cycleDays; requestBudget = cycleBudget;
                pending = OrcaExternalBridge.Enqueue(kind, context.Map.uniqueID, new {
                    cycleDays, cycleBudget, input, instructions = policy?.instructions ?? "Use the normal cycle budget and incident constraints.",
                    colony = new OrcaAgentGameAdapter().CaptureColony(context.Map.uniqueID),
                    resultSchema = "{plan:{summary:string,finalRemainingBudget:0,events:[{offsetDays:number,incidentDef:string,pointsFactor:number,polarity:string,budgetDelta:integer,remainingBudget:integer,reason:string}]}}",
                    rules = "Discover incidents with incidents_list. Use the supplied workflow policy and budget rules. No player-operation tools in narrative mode." },
                    result => Parse(OrcaAgentGameAdapter.StoryContext(requestMap), result["plan"] as JsonObject, requestDays, requestBudget, requestPolicy));
                status = "Waiting for external narrator: " + pending.Id;
            }
            catch (Exception ex) { status = ex.Message; retryAfter = DateTime.UtcNow.AddSeconds(10); }
            return null;
        }
        if (!pending.Result.IsCompleted) return null;
        if (context.Map.uniqueID != requestMap) return null;
        try
        {
            var result = pending.Result.GetAwaiter().GetResult();
            var plan = Parse(context, result["plan"] as JsonObject, requestDays, requestBudget, requestPolicy);
            status = "External cycle plan accepted."; return plan;
        }
        catch (Exception ex) { status = "External plan failed: " + ex.Message; retryAfter = DateTime.UtcNow.AddSeconds(10); return null; }
        finally { pending = null; }
    }
    internal static OrcaIncidentCyclePlan Parse(AiToolContext context, JsonObject json, float days, int budget, IncidentPlanningPolicy suppliedPolicy = null)
    {
        if (json == null || json.ToJsonString().Length > 32000) throw new BridgeException("invalid_plan", "plan must be an object of at most 32000 characters.");
        if (json["events"] is not JsonArray events || events.Count > 24) throw new BridgeException("invalid_plan", "Use at most 24 scheduled events.");
        var plan = LlmIncidentDecisionProvider.TryParseFinalCyclePlan(context, json.ToJsonString(), days, budget, out var reason);
        if (plan == null) throw new BridgeException("invalid_plan", reason);
        int remaining = budget;
        foreach (var incident in plan.incidents)
        {
            long next = (long)remaining + incident.budgetDelta;
            if (next is < 0 or > 100 || next != incident.remainingBudget) throw new BridgeException("invalid_plan", "Budget arithmetic is inconsistent or outside 0-100.");
            remaining = (int)next;
            if (!OrcaIncidentDefCache.TryGet(incident.incidentDefName, out var def) || !def.Def.TargetAllowed(context.target)) throw new BridgeException("invalid_plan", "Incident cannot target this map.");
        }
        var policy = suppliedPolicy ?? OrcaPersonaBehaviors.Current.CreatePlanningPolicy(context, days, budget);
        if (policy != null && !policy.Accepts(plan, out reason)) throw new BridgeException("invalid_plan", reason);
        return plan;
    }
}
