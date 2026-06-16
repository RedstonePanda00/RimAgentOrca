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

    public static class OrcaDecisionProvider
    {
        private static IAiDecisionProvider connectedProvider;

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
            connectedProvider = provider;
        }

        public static void EnsureConnectedProvider()
        {
            if (connectedProvider == null)
            {
                connectedProvider = new LlmIncidentDecisionProvider();
            }
        }

        public static void ClearConnectedProvider()
        {
            connectedProvider = null;
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
