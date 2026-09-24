using Verse;

namespace DeepseekTheOrca
{
    // Minimal chat agent contract so Agent/Memory/Persona layers never
    // reference the Ui chat session singleton directly.
    public interface IOrcaChatAgent
    {
        bool IsBusy { get; }
        bool TryStartProactive(OrcaProactiveConversationRequest request);
        void ClearConversation();
    }

    // Optional capability keeps the original third-party agent contract compatible.
    public interface IOrcaChatAgentUpdate
    {
        void UpdateConversation();
    }

    public interface IOrcaChatAgentDiagnostics
    {
        string RuntimeDiagnostic { get; }
    }

    public static class OrcaChatAgentHub
    {
        private static IOrcaChatAgent agent;
        private static Game owner;
        private static System.DateTime lastUpdate;
        private static int updates;
        private static System.DateTime nextDiagnostic;

        public static string DiagnosticStatus
        {
            get { return "chatOwner=" + (owner == null ? "none" : ReferenceEquals(owner, Current.Game) ? "current" : "different")
                + "; agent=" + (agent == null ? "none" : agent.GetType().FullName)
                + "; updates=" + updates + "; lastUpdate=" + (lastUpdate == default(System.DateTime) ? "never" : (System.DateTime.UtcNow - lastUpdate).TotalSeconds.ToString("F1") + "s ago"); }
        }

        public static void Register(IOrcaChatAgent value)
        {
            if (agent != null && !ReferenceEquals(agent, value)) agent.ClearConversation();
            agent = value;
        }

        internal static void BeginGame(Game game)
        {
            owner = game;
            updates = 0;
            lastUpdate = default(System.DateTime);
            nextDiagnostic = System.DateTime.UtcNow.AddSeconds(20);
            ClearConversation();
            OrcaRuntimeDiagnostics.Record("Chat bound", DiagnosticStatus);
        }

        internal static void Update(Game game)
        {
            if (game == null || !ReferenceEquals(owner, game) || !ReferenceEquals(Current.Game, game)) return;
            lastUpdate = System.DateTime.UtcNow;
            updates++;
            var updatable = agent as IOrcaChatAgentUpdate;
            if (updatable != null) updatable.UpdateConversation();
        }

        internal static void CheckLifetime()
        {
            if (owner == null || ReferenceEquals(Current.Game, owner))
            {
                // The lifetime monitor runs independently of GameComponent.Update;
                // it can diagnose a stalled result consumer without advancing chat.
                if (!IsChatBusy) nextDiagnostic = System.DateTime.UtcNow.AddSeconds(20);
                else if (System.DateTime.UtcNow >= nextDiagnostic)
                {
                    nextDiagnostic = System.DateTime.UtcNow.AddSeconds(20);
                    var diagnostic = agent as IOrcaChatAgentDiagnostics;
                    OrcaRuntimeDiagnostics.Record("Chat waiting watchdog", diagnostic == null ? DiagnosticStatus : diagnostic.RuntimeDiagnostic);
                }
                return;
            }
            owner = null;
            OrcaRuntimeDiagnostics.Record("Chat detached", "game changed; " + DiagnosticStatus);
            ClearConversation();
        }

        public static bool IsChatBusy
        {
            get { return agent != null && agent.IsBusy; }
        }

        public static bool TryStartProactive(OrcaProactiveConversationRequest request)
        {
            return agent != null && agent.TryStartProactive(request);
        }

        public static void ClearConversation()
        {
            if (agent != null)
            {
                agent.ClearConversation();
            }
        }
    }
}
