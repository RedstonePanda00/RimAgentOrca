using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using RimAgent.Core;
using Verse;

namespace RimAgent;

internal static class OrcaExternalConfiguration
{
    private static readonly string[] BooleanFields = ["enableAiPlanning", "debugLogging", "enableWebSearch", "enableSemanticToolSearch", "enableLongTermMemory", "enableSemanticMemoryQuery", "enableHttpMcp"];
    private static readonly string[] Models = ["model", "controllerModel", "decisionModel", "dialogueModel", "toolModel", "visionModel", "webSearchModel", "memoryModel", "embeddingModel"];
    private static readonly Dictionary<string, (int Min, int Max)> Integers = new() {
        ["tavilyMaxResults"] = (1, 10), ["maxToolCalls"] = (1, 32), ["maxToolResultEstimatedTokens"] = (200, 4000),
        ["toolSearchTopK"] = (1, 12), ["memoryMaxInjectedEntries"] = (0, 20), ["knowledgeMaxInjectedEntries"] = (0, 20) };
    private static IEnumerable<string> Names => BooleanFields.Concat(Models).Concat(Integers.Keys).Concat(["planningMtbDays", "webSearchMode", "tavilySearchDepth", "enabledDefTools", "disabledDefTools"]);
    private static FieldInfo Field(string name) => typeof(RimAgentSettings).GetField(name);
    public static object Read()
    {
        var settings = RimAgentMod.Settings;
        return new { values = Names.ToDictionary(n => n, n => Field(n).GetValue(settings)),
            bounds = Integers.ToDictionary(p => p.Key, p => new { min = p.Value.Min, max = p.Value.Max }),
            modelFields = Models, planningMtbDays = new { min = .1, max = 60 },
            searchProviders = new[] { "tavily", "deepseek" }, searchDepths = new[] { "basic", "advanced", "fast", "ultra-fast" },
            connections = settings.llmConnections.Select(c => new { c.id, c.name, c.enabled, c.provider, models = c.availableModels }).ToArray(),
            toolDefinitions = DefDatabase<OrcaToolDef>.AllDefsListForReading.Select(d => new { d.defName, d.label }).ToArray(),
            credentials = "Connections, keys, endpoints, proxies and bridge enablement are player-managed and are not editable through this API." };
    }
    public static object Update(JsonObject args)
    {
        if (args["settings"] is not JsonObject changes || changes.Count == 0 || changes.Count > 32) throw new BridgeException("invalid_settings", "settings must contain 1-32 editable fields.");
        var settings = RimAgentMod.Settings;
        var prepared = new Dictionary<string, object>();
        foreach (var (key, value) in changes)
        {
            if (!Names.Contains(key)) throw new BridgeException("setting_forbidden", "Setting is not editable: " + key);
            if (key is "enabledDefTools" or "disabledDefTools")
            {
                if (value is not JsonArray list || list.Count > 256) throw new BridgeException("invalid_settings", "Tool overrides must be an array of at most 256 defNames.");
                var names = list.Select(n => n?.GetValue<string>()).Distinct().ToList();
                if (names.Any(n => n == null || DefDatabase<OrcaToolDef>.GetNamedSilentFail(n) == null)) throw new BridgeException("invalid_settings", "Unknown tool defName.");
                prepared[key] = names;
            }
            else if (BooleanFields.Contains(key)) prepared[key] = changes.Flag(key);
            else if (Integers.TryGetValue(key, out var bound)) prepared[key] = changes.Number(key, bound.Min, bound.Max);
            else if (Models.Contains(key))
            {
                var reference = value?.GetValue<string>();
                if (reference == null || reference.Length > 256) throw new BridgeException("invalid_model", "Model reference must be a string, empty to clear.");
                if (reference != "")
                {
                    var parts = reference.Split('|', 2);
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]) || !settings.llmConnections.Any(c => c.id == parts[0]))
                        throw new BridgeException("invalid_model", "Use existingConnectionId|modelName. Connections must be configured by the player.");
                }
                prepared[key] = reference;
            }
            else if (key == "planningMtbDays")
            {
                float days = value?.GetValue<float>() ?? 0;
                if (!float.IsFinite(days) || days < .1f || days > 60) throw new BridgeException("invalid_settings", "planningMtbDays must be 0.1-60.");
                prepared[key] = days;
            }
            else
            {
                var text = changes.Text(key, 32);
                if (key == "webSearchMode" ? text is not ("tavily" or "deepseek") : text is not ("basic" or "advanced" or "fast" or "ultra-fast")) throw new BridgeException("invalid_settings", "Invalid " + key);
                prepared[key] = text;
            }
        }
        var prior = prepared.Keys.ToDictionary(k => k, k => Field(k).GetValue(settings));
        try
        {
            foreach (var (key, value) in prepared) Field(key).SetValue(settings, value);
            RimAgentMod.Instance.WriteSettings();
        }
        catch { foreach (var (key, value) in prior) Field(key).SetValue(settings, value); throw; }
        return Read();
    }
}
