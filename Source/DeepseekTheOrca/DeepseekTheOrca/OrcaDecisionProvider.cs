namespace DeepseekTheOrca
{
    public interface IAiDecisionProvider
    {
        bool HasPendingWork { get; }
        AiIncidentPlan SelectIncidentPlan(AiToolContext context);
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

        public static void SetConnectedProvider(IAiDecisionProvider provider)
        {
            connectedProvider = provider;
        }

        public static void ClearConnectedProvider()
        {
            connectedProvider = null;
        }

        public static AiIncidentPlan SelectIncidentPlan(AiToolContext context)
        {
            if (!IsAvailable)
            {
                return null;
            }

            return connectedProvider.SelectIncidentPlan(context);
        }
    }
}
