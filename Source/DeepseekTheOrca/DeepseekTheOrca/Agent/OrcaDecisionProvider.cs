namespace DeepseekTheOrca
{
    using System.Collections.Generic;

    public interface IAiDecisionProvider
    {
        bool HasPendingWork { get; }
        string LastStatus { get; }
        IEnumerable<string> LogLines { get; }
        OrcaIncidentCyclePlan SelectIncidentCyclePlan(AiToolContext context, float cycleDays, int cycleBudget);
    }

    // Optional lifetime contract for planners that own asynchronous work.
    public interface IOrcaCancellableDecisionProvider
    {
        void CancelPendingWork();
    }

    public static class OrcaDecisionProvider
    {
        private static IAiDecisionProvider connectedProvider;
        private static long nextConfigurationCheck;

        public static bool IsAvailable
        {
            get
            {
                DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
                return settings != null && settings.HasConfiguredLlm && connectedProvider != null;
            }
        }

        public static bool HasPendingWork
        {
            get { return connectedProvider != null && connectedProvider.HasPendingWork; }
        }

        public static bool HasConnectedProvider
        {
            get { return connectedProvider != null; }
        }

        public static string LastStatus
        {
            get { return connectedProvider == null ? "" : connectedProvider.LastStatus; }
        }

        public static IEnumerable<string> LogLines
        {
            get { return connectedProvider == null ? new List<string>() : connectedProvider.LogLines; }
        }

        public static void SetConnectedProvider(IAiDecisionProvider provider)
        {
            if (!ReferenceEquals(connectedProvider, provider))
            {
                var cancellable = connectedProvider as IOrcaCancellableDecisionProvider;
                if (cancellable != null) cancellable.CancelPendingWork();
            }
            connectedProvider = provider;
        }

        public static void EnsureConnectedProvider()
        {
            if (connectedProvider == null)
            {
                connectedProvider = new LlmIncidentDecisionProvider();
            }
        }

        // Called on the game thread. Transport health reports never replace a
        // workflow owner or cancel a planner from an HTTP continuation thread.
        internal static void UpdateConfiguration()
        {
            var settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !settings.enableAiPlanning)
            {
                if (connectedProvider != null) ClearConnectedProvider();
                return;
            }
            long now = System.DateTime.UtcNow.Ticks;
            if (now < nextConfigurationCheck) return;
            nextConfigurationCheck = now + System.TimeSpan.TicksPerSecond;
            if (settings.HasConfiguredLlm) EnsureConnectedProvider();
            else if (connectedProvider != null) ClearConnectedProvider();
        }

        public static void ClearConnectedProvider()
        {
            SetConnectedProvider(null);
            nextConfigurationCheck = 0;
        }

        public static OrcaIncidentCyclePlan SelectIncidentCyclePlan(AiToolContext context, float cycleDays, int cycleBudget)
        {
            if (!IsAvailable)
            {
                return null;
            }

            return connectedProvider.SelectIncidentCyclePlan(context, cycleDays, cycleBudget);
        }
    }
}
