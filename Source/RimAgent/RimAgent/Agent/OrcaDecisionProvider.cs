namespace RimAgent
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
                RimAgentSettings settings = RimAgentMod.Settings;
                return connectedProvider != null && (OrcaExternalBridge.IsControlled || settings != null && settings.HasConfiguredLlm);
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
                connectedProvider = OrcaPlanningWorkforce.Create();
            }
        }

        // Called on the game thread. Transport health reports never replace a
        // workflow owner or cancel a planner from an HTTP continuation thread.
        internal static void UpdateConfiguration()
        {
            if (OrcaExternalBridge.IsControlled)
            {
                if (connectedProvider is not ExternalIncidentDecisionProvider) SetConnectedProvider(OrcaPlanningWorkforce.Create());
                var plan = ((ExternalIncidentDecisionProvider)connectedProvider).ConsumeCompleted(out var context, out var days);
                if (plan != null) OrcaIncidentSchedule.StoreCyclePlan(plan, context, days);
                return;
            }
            if (connectedProvider is ExternalIncidentDecisionProvider) ClearConnectedProvider();
            var settings = RimAgentMod.Settings;
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
            if (OrcaPersonaBehaviors.Current.BlocksNormalPlanning) return null;
            if (!IsAvailable)
            {
                return null;
            }

            return connectedProvider.SelectIncidentCyclePlan(context, cycleDays, cycleBudget);
        }
    }
}
