using System;
using System.Threading;

namespace DeepseekTheOrca
{
    public sealed partial class LlmStreamingChatRequest
    {
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private readonly DateTime startedAt = DateTime.UtcNow;
        private readonly string diagnosticId = Guid.NewGuid().ToString("N").Substring(0, 8);
        private DateTime lastReceived;
        private string stage = "queued";
        private string finishReason = "";
        private int receivedLines;
        internal CancellationToken CancellationToken { get { return cancellationSource.Token; } }
        internal string FinishReason { get { lock (syncRoot) return finishReason; } }

        // Metadata only: never log credentials, prompts or generated dialogue.
        public string DiagnosticStatus
        {
            get
            {
                lock (syncRoot) return "request=" + diagnosticId + "; stage=" + stage
                    + "; completed=" + completed + "; cancelled=" + cancelled
                    + "; age=" + (DateTime.UtcNow - startedAt).TotalSeconds.ToString("F1") + "s"
                    + "; lines=" + receivedLines + "; rawChars=" + rawContent.Length + "; visibleChars=" + visibleText.Length
                    + "; lastReceive=" + (lastReceived == default(DateTime) ? "never" : (DateTime.UtcNow - lastReceived).TotalSeconds.ToString("F1") + "s ago")
                    + "; finish=" + finishReason;
            }
        }

        internal void SetStage(string value)
        {
            lock (syncRoot) stage = value;
            OrcaRuntimeDiagnostics.Record("Chat stream", DiagnosticStatus);
        }

        internal void ReceivedLine()
        {
            lock (syncRoot) { receivedLines++; lastReceived = DateTime.UtcNow; }
        }

        internal void SetFinishReason(string value)
        {
            if (!string.IsNullOrEmpty(value)) lock (syncRoot) finishReason = value;
        }
    }
}
