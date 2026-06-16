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

    public static class OrcaChatAgentHub
    {
        private static IOrcaChatAgent agent;

        public static void Register(IOrcaChatAgent value)
        {
            agent = value;
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
