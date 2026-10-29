using System;
using System.Text.Json.Nodes;
using System.Threading;
using RimAgent.Core;
using Verse;

namespace RimAgent;

internal static class OrcaExternalBridge
{
    private static AgentRuntime runtime;
    private static CommandDispatcher dispatcher;
    private static PipeBridge server;
    private static Game readyOwner, boundOwner;
    private static bool testGameQueued;
    private static bool? previousRunInBackground;
    private static volatile bool controlled;
    public static bool IsControlled => controlled;
    public static RelayRequest Enqueue(string kind, int? mapId, object context, Action<JsonObject> validate = null) => runtime.EnqueueRequest(kind, mapId, context, validate);
    public static string PipeName => Environment.GetEnvironmentVariable("RIMAGENT_PIPE") ?? BridgeWire.DefaultPipeName;
    public static string Status => server == null ? "stopped" : "running: " + PipeName;
    public static void NotifyReady(Game game) => Volatile.Write(ref readyOwner, game);

    public static void Update()
    {
        bool enabled = RimAgentMod.Settings?.enableExternalAgentBridge == true || Environment.GetEnvironmentVariable("RIMAGENT_BRIDGE") == "1";
        if (!enabled) { Stop(); return; }
        // Waylith suspends Update itself when unfocused, including the command pump.
        // Keep bridge transport responsive without changing the player's saved preferences.
        previousRunInBackground ??= UnityEngine.Application.runInBackground;
        UnityEngine.Application.runInBackground = true;
        if (server == null)
        {
            runtime = new AgentRuntime(new OrcaAgentGameAdapter());
            runtime.ControlAcquired = mode => {
                if (mode != "narrative") return;
                OrcaChatAgentHub.PrepareExternalControl(); OrcaDecisionProvider.ClearConnectedProvider();
                GeminiHissService.PrepareExternalControl();
            };
            dispatcher = new CommandDispatcher();
            server = new PipeBridge(PipeName, dispatcher, message => OrcaRuntimeDiagnostics.Record("Agent bridge", message));
            OrcaRuntimeDiagnostics.Record("Agent bridge started", PipeName);
        }
        var ready = Volatile.Read(ref readyOwner);
        if (boundOwner != null && (!ReferenceEquals(boundOwner, Current.Game) || Current.ProgramState != ProgramState.Playing))
        { runtime.End(); boundOwner = null; controlled = false; }
        if (!LongEventHandler.ShouldWaitForEvent && Current.ProgramState == ProgramState.Playing && ready != null && ReferenceEquals(ready, Current.Game) && !ReferenceEquals(boundOwner, ready))
        {
            boundOwner = ready; runtime.Begin();
            if (testGameQueued) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            testGameQueued = false;
        }
        if (LongEventHandler.ShouldWaitForEvent) return;
        runtime.Pump();
        dispatcher.Pump(Execute);
        controlled = runtime.IsNarrativeControlled;
    }
    private static ToolReply Execute(string tool, JsonObject args)
    {
        if (tool == "rimagent_test_relay")
        {
            if (Environment.GetEnvironmentVariable("RIMAGENT_ALLOW_TEST_GAME") != "1" || Current.ProgramState != ProgramState.Playing)
                return ToolReply.Fail("test_game_disabled", "Only available in an explicitly launched test game.");
            if (!runtime.IsNarrativeControlled) return ToolReply.Fail("no_narrator", "Acquire a narrative controller first.");
            var kind = args.Text("kind", 16);
            if (kind == "chat") OrcaChatWindowManager.Session.Send(args.Text("text", 8000));
            else if (kind == "chat_probe") OrcaChatAcceptanceProbe.Install();
            else if (kind == "chat_clear") OrcaChatWindowManager.Session.Clear();
            else if (kind == "proactive")
            {
                if (!OrcaChatWindowManager.Session.TryStartProactive(new OrcaProactiveConversationRequest("acceptance_probe", "Isolated trigger", args.Text("text", 8000)) { openChatWindow = false }))
                    return ToolReply.Fail("busy", "Proactive turn did not start.");
            }
            else if (kind is "gemini" or "orca")
            {
                GeminiHissService.ResetPersonaState();
                RimAgentMod.Settings.chatPersonaDefName = kind == "gemini" ? OrcaChatPersonaManager.BuiltInGeminiId : OrcaChatPersonaManager.BuiltInOrcaId;
                if (kind == "gemini")
                {
                    var p = GeminiHissService.Settings;
                    p.eventCount = 2; p.budget = 2; p.minGapHours = p.maxGapHours = p.cooldownHours = 1;
                }
            }
            else if (kind == "story")
            {
                OrcaDecisionProvider.UpdateConfiguration();
                var context = OrcaAgentGameAdapter.StoryContext(Find.CurrentMap.uniqueID);
                OrcaDecisionProvider.SelectIncidentCyclePlan(context, RimAgentMod.Settings.planningMtbDays, OrcaIncidentCyclePlan.DefaultCycleBudget);
            }
            else if (kind != "inspect") return ToolReply.Fail("invalid_arguments", "Unknown isolated test fixture.");
            return ToolReply.Ok(new { providerConfigured = RimAgentMod.Settings.HasConfiguredLlm, lastReply = OrcaChatWindowManager.Session.LastReplyText,
                displayText = OrcaChatWindowManager.Session.DisplayText, waiting = OrcaChatWindowManager.Session.IsWaiting, schedule = OrcaIncidentSchedule.DebugText(), chatProbe = OrcaChatAcceptanceProbe.Snapshot() });
        }
        if (tool != "rimagent_test_game_start") return runtime.Execute(tool, args);
        if (Environment.GetEnvironmentVariable("RIMAGENT_ALLOW_TEST_GAME") != "1") return ToolReply.Fail("test_game_disabled", "Launch explicitly with --allow-test-game.");
        if (Current.Game != null || Current.ProgramState != ProgramState.Entry || testGameQueued)
            return ToolReply.Fail("game_exists", "Test game creation requires an empty main menu and no pending creation.");
        testGameQueued = true;
        LongEventHandler.QueueLongEvent(() =>
        {
            Root_Play.SetupForQuickTestPlay();
            if (args["storyteller"] != null)
            {
                var def = DefDatabase<RimWorld.StorytellerDef>.GetNamed(args.Text("storyteller", 128));
                Current.Game.storyteller = new RimWorld.Storyteller(def, RimWorld.DifficultyDefOf.Rough);
            }
            RimWorld.PageUtility.InitGameStart();
        }, "GeneratingWorld", true, ex => { testGameQueued = false; Log.Error("[RimAgent] Test game creation failed: " + ex.Message); });
        return ToolReply.Ok(new { accepted = true, readiness = "Poll session_get until ready." });
    }
    private static void Stop()
    {
        if (previousRunInBackground is bool previous)
        {
            UnityEngine.Application.runInBackground = previous;
            previousRunInBackground = null;
        }
        if (server == null) return;
        dispatcher.Close(); server.Dispose(); runtime.End();
        dispatcher = null; server = null; runtime = null; boundOwner = null; controlled = false;
    }
}
