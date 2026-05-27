namespace DeepseekTheOrca
{
    public enum OrcaAgentPhase
    {
        Unknown,
        Routing,
        ToolGathering,
        FinalReply,
        NeedsMoreTools,
        Completed,
        Failed
    }

    public sealed class OrcaAgentNodeSpec
    {
        public string id = "";
        public string label = "";
        public string description = "";
        public OrcaLlmModelRole role = OrcaLlmModelRole.Fallback;
        public bool canUseTools;
        public bool canStream;
    }

    public sealed class OrcaAgentPhaseContext
    {
        public readonly OrcaChatSession session;
        public readonly OrcaAgentPhase phase;
        public readonly OrcaLlmModelRole role;
        public readonly int toolRoundsUsed;
        public readonly bool streaming;
        public readonly string reason;

        public OrcaAgentPhaseContext(OrcaChatSession session, OrcaAgentPhase phase, OrcaLlmModelRole role, int toolRoundsUsed, bool streaming, string reason)
        {
            this.session = session;
            this.phase = phase;
            this.role = role;
            this.toolRoundsUsed = toolRoundsUsed;
            this.streaming = streaming;
            this.reason = reason ?? "";
        }
    }

    public sealed class OrcaAgentRoutingContext
    {
        public readonly OrcaChatSession session;
        public readonly string originalRoute;
        public readonly OrcaLlmModelRole originalRole;
        public string route;
        public OrcaLlmModelRole requestedRole;
        public string reason;

        public OrcaAgentRoutingContext(OrcaChatSession session, string route, OrcaLlmModelRole role, string reason)
        {
            this.session = session;
            this.originalRoute = route ?? "";
            this.originalRole = role;
            this.route = route ?? "";
            this.requestedRole = role;
            this.reason = reason ?? "";
        }

        public bool Changed
        {
            get { return requestedRole != originalRole || route != originalRoute; }
        }
    }
}
