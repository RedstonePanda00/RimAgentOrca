using System;
using System.Linq;
using System.Text.Json.Nodes;
using RimAgent.Core;
using RimWorld;
using Verse;

namespace RimAgent;

internal sealed partial class OrcaAgentGameAdapter : IAgentRelay
{
    internal static AiToolContext StoryContext(int mapId)
    {
        var comp = OrcaStorytellerUtility.ActiveOrcaComp() ?? throw new BridgeException("storyteller_unavailable", "Select a storyteller containing RimAgent before scheduling its events.");
        return new AiToolContext(Map(mapId), comp, comp.props as StorytellerCompProperties_DeepseekOrca);
    }
    private static object StoryRead(string tool, JsonObject a)
    {
        var context = StoryContext(a.Number("mapId", 0, int.MaxValue));
        if (tool == "rimagent_incidents_list") return new { items = OrcaIncidentDefCache.AvailableFor(context).Select(d => new {
            defName = d.DefName, label = d.Label, category = d.Category, polarity = d.Polarity, budgetHint = d.BudgetHint }).ToArray() };
        return new { active = true, externalNarrator = OrcaExternalBridge.IsControlled,
            cycleDays = RimAgentMod.Settings.planningMtbDays, cycleBudget = OrcaIncidentCyclePlan.DefaultCycleBudget,
            schedule = OrcaIncidentSchedule.DebugText(), status = OrcaDecisionProvider.LastStatus };
    }
    private static object ApplyStory(JsonObject a)
    {
        GeminiHissService.RequireNormalPlanAllowed();
        var context = StoryContext(a.Number("mapId", 0, int.MaxValue));
        var days = RimAgentMod.Settings.planningMtbDays;
        var plan = ExternalIncidentDecisionProvider.Parse(context, a["plan"] as JsonObject, days, OrcaIncidentCyclePlan.DefaultCycleBudget);
        OrcaDecisionProvider.ClearConnectedProvider();
        OrcaIncidentSchedule.StoreCyclePlan(plan, context, days);
        return new { accepted = true, planId = plan.planId, events = plan.incidents.Count, schedule = OrcaIncidentSchedule.DebugText() };
    }
    private static object AppeaseGemini(JsonObject a)
    {
        var mapId = a.Number("mapId", 0, int.MaxValue);
        Map(mapId);
        if (GeminiHissService.State?.target?.uniqueID != mapId) throw new BridgeException("wrong_map", "Use the hiss sequence's map.");
        var result = GeminiHissService.Appease(a.Text("reason", 500));
        if (!result.success) throw new BridgeException("reconciliation_unavailable", "Gemini must be sulking before he can be comforted.");
        return GeminiHissService.ExternalStatus(mapId);
    }
    private static object BeginHiss(JsonObject a)
    {
        var mapId = a.Number("mapId", 0, int.MaxValue);
        var map = Map(mapId);
        if (!GeminiHissService.CanBegin(map, out var reason)) throw new BridgeException("hiss_unavailable", reason);
        GeminiHissService.Begin(map, a.Text("reason", 500));
        if (!GeminiHissService.IsPlanning) throw new BridgeException("hiss_unavailable", "Hiss planning did not start.");
        return GeminiHissService.ExternalStatus(mapId);
    }
    private static object PostNarration(JsonObject a)
    {
        OrcaChatAgentHub.PostExternal(a.Text("text", 16000)); return new { posted = true };
    }
    public void ValidateRelayResult(string kind, int? mapId, JsonObject context, JsonObject result)
    {
        // Planning requests carry their workflow's validator in RelayRequest, shared by root and child submissions.
        if (kind is not ("story_cycle" or "gemini_hiss")) result.Text("text", 16000);
    }
}
