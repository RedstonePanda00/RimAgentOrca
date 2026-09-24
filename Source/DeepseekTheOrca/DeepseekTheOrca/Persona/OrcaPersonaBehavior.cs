using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace DeepseekTheOrca
{
    // Stateless policies; per-game state belongs to the game's components.
    public abstract class OrcaPersonaBehavior
    {
        public virtual string RuntimePrompt { get { return ""; } }
        public virtual bool ForceDialogue { get { return false; } }
        public virtual bool BlocksNormalPlanning { get { return false; } }
        public virtual string ExecutionBlockReason { get { return ""; } }
        public virtual string SettingsLabel { get { return ""; } }
        public virtual Window CreateSettingsWindow() { return null; }
        public virtual bool WantsToolJudgment(DeepseekTheOrcaSettings settings, bool allowExecution) { return false; }
        public virtual void Deselected() { }
        public virtual void Selected() { }
        public virtual void GameStarted(Game game, bool loaded) { }
        public virtual void GameEnded(Game game) { }
        public virtual void Update() { }
        public virtual void Tick() { }
        public virtual IncidentPlanningPolicy CreatePlanningPolicy(AiToolContext context, float cycleDays, int budget) { return null; }
    }

    public static class OrcaPersonaBehaviors
    {
        private sealed class DefaultBehavior : OrcaPersonaBehavior { }
        private static readonly OrcaPersonaBehavior fallback = new DefaultBehavior();
        private static readonly Dictionary<string, OrcaPersonaBehavior> behaviors = new Dictionary<string, OrcaPersonaBehavior>();
        private static readonly List<OrcaPersonaBehavior> lifecycle = new List<OrcaPersonaBehavior>();
        static OrcaPersonaBehaviors() { Register(OrcaChatPersonaManager.BuiltInGeminiId, new GeminiBehavior()); }

        public static string NormalizeId(string id)
        { return id != null && id.StartsWith(OrcaChatPersonaManager.DefPrefix, StringComparison.Ordinal) ? id.Substring(OrcaChatPersonaManager.DefPrefix.Length) : id; }

        public static OrcaPersonaBehavior Current { get { return For(OrcaChatPersonaManager.CurrentPersonaId()); } }
        public static OrcaPersonaBehavior For(string id)
        {
            id = NormalizeId(id);
            if (string.IsNullOrWhiteSpace(id)) return fallback;
            OrcaPersonaBehavior result;
            if (behaviors.TryGetValue(id, out result)) return result;
            var def = DefDatabase<OrcaChatPersonaDef>.GetNamedSilentFail(id);
            if (def == null || def.behaviorClass == null) return fallback;
            try { Register(id, (OrcaPersonaBehavior)Activator.CreateInstance(def.behaviorClass)); }
            catch (Exception ex)
            {
                Log.Error("[RimAgent] Cannot create persona behavior " + id + ": " + ex);
                behaviors[id] = fallback;
            }
            return behaviors[id];
        }
        public static void Register(string personaId, OrcaPersonaBehavior behavior)
        {
            if (string.IsNullOrWhiteSpace(personaId) || behavior == null) throw new ArgumentException("Persona ID and behavior are required.");
            var guarded = new GuardedPersonaBehavior(personaId, behavior);
            behaviors.Add(NormalizeId(personaId), guarded);
            lifecycle.Add(guarded);
        }
        public static void Switching(string from, string to)
        {
            if (NormalizeId(from) == NormalizeId(to)) return;
            For(from).Deselected();
            For(to).Selected();
        }
        internal static void GameStarted(Game game, bool loaded)
        {
            foreach (var def in DefDatabase<OrcaChatPersonaDef>.AllDefsListForReading)
                if (def.behaviorClass != null) For(def.defName);
            int count = lifecycle.Count;
            for (int i = 0; i < count; i++) lifecycle[i].GameStarted(game, loaded);
        }
        internal static void GameEnded(Game game)
        { int count = lifecycle.Count; for (int i = 0; i < count; i++) lifecycle[i].GameEnded(game); }
        internal static void Update()
        { int count = lifecycle.Count; for (int i = 0; i < count; i++) lifecycle[i].Update(); }
        public static void Tick()
        { int count = lifecycle.Count; for (int i = 0; i < count; i++) lifecycle[i].Tick(); }
    }

    // A faulty submod must not stop all other personas or the game component.
    internal sealed class GuardedPersonaBehavior : OrcaPersonaBehavior
    {
        private readonly string id;
        private readonly OrcaPersonaBehavior inner;
        private readonly HashSet<string> failures = new HashSet<string>();
        public GuardedPersonaBehavior(string id, OrcaPersonaBehavior inner) { this.id = id; this.inner = inner; }
        private T Read<T>(string hook, Func<T> action, T fallback)
        {
            if (failures.Contains(hook)) return fallback;
            try { return action(); }
            catch (Exception ex) { failures.Add(hook); Log.Error("[RimAgent] Persona " + id + " hook " + hook + " paused: " + ex); return fallback; }
        }
        private void Run(string hook, Action action) { Read(hook, () => { action(); return true; }, false); }
        public override string RuntimePrompt { get { return Read("prompt", () => inner.RuntimePrompt, ""); } }
        public override bool ForceDialogue { get { return Read("dialogue", () => inner.ForceDialogue, true); } }
        public override bool BlocksNormalPlanning { get { return Read("planning", () => inner.BlocksNormalPlanning, true); } }
        public override string ExecutionBlockReason { get { return Read("execution", () => inner.ExecutionBlockReason, "Persona rule is unavailable"); } }
        public override string SettingsLabel { get { return Read("settingsLabel", () => inner.SettingsLabel, ""); } }
        public override Window CreateSettingsWindow() { return Read("settingsWindow", () => inner.CreateSettingsWindow(), (Window)null); }
        public override bool WantsToolJudgment(DeepseekTheOrcaSettings settings, bool allowExecution)
        { return Read("tools", () => inner.WantsToolJudgment(settings, allowExecution), false); }
        public override void Selected() { Run("selected", inner.Selected); }
        public override void Deselected() { Run("deselected", inner.Deselected); }
        public override void GameStarted(Game game, bool loaded) { failures.Clear(); Run("start", () => inner.GameStarted(game, loaded)); }
        public override void GameEnded(Game game) { Run("end", () => inner.GameEnded(game)); }
        public override void Tick() { Run("tick", inner.Tick); }
        public override void Update() { Run("update", inner.Update); }
        public override IncidentPlanningPolicy CreatePlanningPolicy(AiToolContext context, float cycleDays, int budget)
        { return Read("planningPolicy", () => inner.CreatePlanningPolicy(context, cycleDays, budget), (IncidentPlanningPolicy)null); }
    }

    internal sealed class GeminiBehavior : OrcaPersonaBehavior
    {
        public override string RuntimePrompt { get { return GeminiHissService.RuntimePrompt(); } }
        public override bool ForceDialogue { get { return GeminiHissService.IsCoolingDown; } }
        public override bool BlocksNormalPlanning { get { return GeminiHissService.IsPlanning; } }
        public override string ExecutionBlockReason
        { get { return ForceDialogue ? "Gemini is sulking and will not execute chat requests" : ""; } }
        public override string SettingsLabel { get { return "DTO_GeminiSettings".Translate(); } }
        public override Window CreateSettingsWindow() { return new GeminiSettingsWindow(); }
        public override void Deselected() { GeminiHissService.ResetPersonaState(); }
        public override void GameEnded(Game game)
        {
            var state = OrcaModuleStorage.ForGame(game).Get<GeminiHissState>(GeminiHissService.StorageId);
            if (state.planner != null) state.planner.CancelPendingWork();
        }
        public override void Tick() { GeminiHissService.Tick(); }
        public override bool WantsToolJudgment(DeepseekTheOrcaSettings settings, bool allowExecution)
        {
            string reason;
            return allowExecution && settings.HasModelForRole(OrcaLlmModelRole.Tool)
                && AiStoryToolRegistry.IsExposedToChat(GeminiPersona.ToolName)
                && GeminiHissService.CanBegin(Find.CurrentMap, out reason);
        }
    }
}
