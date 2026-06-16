using System.Threading.Tasks;
using Verse;

namespace DeepseekTheOrca
{
    public enum LlmConnectionStatus
    {
        NotTested,
        Testing,
        Succeeded,
        Failed
    }

    public static class LlmConnectionTester
    {
        private static readonly object syncRoot = new object();
        private static LlmConnectionStatus status = LlmConnectionStatus.NotTested;
        private static string message = "DTO_ConnectionNotTested";
        private static Task activeTask;

        public static bool IsTesting
        {
            get
            {
                lock (syncRoot)
                {
                    return activeTask != null && !activeTask.IsCompleted;
                }
            }
        }

        public static void Reset()
        {
            lock (syncRoot)
            {
                if (status != LlmConnectionStatus.Testing)
                {
                    status = LlmConnectionStatus.NotTested;
                    message = "DTO_ConnectionNotTested";
                    OrcaDecisionProvider.ClearConnectedProvider();
                }
            }
        }

        public static void Start(string apiKey, string model)
        {
            Start(DeepseekTheOrcaMod.Settings);
        }

        public static void Start(DeepseekTheOrcaSettings settings)
        {
            lock (syncRoot)
            {
                if (activeTask != null && !activeTask.IsCompleted)
                {
                    return;
                }

                status = LlmConnectionStatus.Testing;
                message = "DTO_ConnectionTesting";
            }

            activeTask = Task.Run(async delegate
            {
                LlmConnectionTestResult result = await new LlmApiClient().TestConnectionAsync(settings).ConfigureAwait(false);
                lock (syncRoot)
                {
                    status = result.success ? LlmConnectionStatus.Succeeded : LlmConnectionStatus.Failed;
                    message = result.message;
                    if (result.success)
                    {
                        OrcaDecisionProvider.SetConnectedProvider(new LlmIncidentDecisionProvider());
                    }
                    else
                    {
                        OrcaDecisionProvider.ClearConnectedProvider();
                    }
                }

                if (result.success)
                {
                    Log.Message("[RimAgent] LLM connection test succeeded.");
                }
                else
                {
                    Log.Warning("[RimAgent] LLM connection test failed: " + result.message);
                }
            });
        }

        public static void ReportSuccessfulCall(string messageText)
        {
            lock (syncRoot)
            {
                if (status != LlmConnectionStatus.Testing)
                {
                    status = LlmConnectionStatus.Succeeded;
                    message = messageText.NullOrEmpty() ? "Connection succeeded." : messageText;
                    OrcaDecisionProvider.EnsureConnectedProvider();
                }
            }
        }

        public static void ReportFailedCall(string messageText)
        {
            lock (syncRoot)
            {
                if (status != LlmConnectionStatus.Testing)
                {
                    status = LlmConnectionStatus.Failed;
                    message = messageText ?? "";
                    OrcaDecisionProvider.ClearConnectedProvider();
                }
            }
        }

        public static void Snapshot(out LlmConnectionStatus currentStatus, out string currentMessage)
        {
            lock (syncRoot)
            {
                currentStatus = status;
                currentMessage = message;
            }
        }
    }
}
