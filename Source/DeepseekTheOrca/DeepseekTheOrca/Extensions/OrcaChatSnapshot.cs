namespace DeepseekTheOrca
{
    /// <summary>Copied chat state for extension callbacks; no mutable session or game references.</summary>
    public sealed class OrcaChatSnapshot
    {
        public bool IsWaiting { get; private set; }
        public int ConversationVersion { get; private set; }
        public string LastUserText { get; private set; }
        public string LastReplyText { get; private set; }
        public string LastProcessText { get; private set; }
        public string LastErrorText { get; private set; }
        public string StatusText { get; private set; }
        public string LastControllerRoute { get; private set; }
        public string CurrentModelRoleLabel { get; private set; }
        public string CurrentModelReference { get; private set; }
        public int TotalToolCalls { get; private set; }
        public int FailedToolCalls { get; private set; }
        public string LastToolName { get; private set; }
        public string LastToolResult { get; private set; }

        internal OrcaChatSnapshot(OrcaChatSession session)
        {
            IsWaiting = session != null && session.IsWaiting;
            ConversationVersion = session == null ? 0 : session.ConversationVersion;
            LastUserText = session == null ? "" : session.LastUserText;
            LastReplyText = session == null ? "" : session.LastReplyText;
            LastProcessText = session == null ? "" : session.LastProcessText;
            LastErrorText = session == null ? "" : session.LastErrorText;
            StatusText = session == null ? "" : session.StatusText;
            LastControllerRoute = session == null ? "" : session.LastControllerRoute;
            CurrentModelRoleLabel = session == null ? "" : session.CurrentModelRoleLabel;
            CurrentModelReference = session == null ? "" : session.CurrentModelReference;
            TotalToolCalls = session == null ? 0 : session.TotalToolCalls;
            FailedToolCalls = session == null ? 0 : session.FailedToolCalls;
            LastToolName = session == null ? "" : session.LastToolName;
            LastToolResult = session == null ? "" : session.LastToolResult;
        }
    }
}
