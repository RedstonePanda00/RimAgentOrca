using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaLlmConnectionManager
    {
        private static readonly object syncRoot = new object();
        private static readonly Dictionary<string, Task<OrcaModelDiscoveryResult>> activeTasks = new Dictionary<string, Task<OrcaModelDiscoveryResult>>();

        public static void Tick(DeepseekTheOrcaSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.EnsureLlmConnections();
            CompleteFinishedTasks(settings);

            foreach (OrcaLlmConnectionSettings connection in settings.llmConnections)
            {
                if (connection == null || !connection.CanDiscoverModels || connection.status != "notTested")
                {
                    continue;
                }

                Start(connection);
            }
        }

        public static void Start(OrcaLlmConnectionSettings connection)
        {
            if (connection == null || !connection.CanDiscoverModels)
            {
                return;
            }

            connection.Normalize();
            lock (syncRoot)
            {
                Task<OrcaModelDiscoveryResult> existing;
                if (activeTasks.TryGetValue(connection.id, out existing) && existing != null && !existing.IsCompleted)
                {
                    return;
                }

                connection.status = "testing";
                connection.message = "DTO_ConnectionTesting";
                string name = connection.name;
                string provider = connection.provider;
                string apiKey = connection.apiKey;
                string baseUrl = connection.ActiveBaseUrl;
                string organization = connection.openAiOrganization;
                string project = connection.openAiProject;
                string proxyUrl = connection.proxyUrl;
                Log.Message("[Deepseek The Orca] Testing LLM API connection '" + name + "' (" + LlmProviderConfig.Profile(provider).label + ") at " + baseUrl + (proxyUrl.NullOrEmpty() ? "." : " via proxy " + proxyUrl + "."));
                activeTasks[connection.id] = Task.Run(async delegate
                {
                    return await new LlmApiClient().ListModelsAsync(apiKey, baseUrl, organization, project, proxyUrl).ConfigureAwait(false);
                });
            }
        }

        public static bool IsTesting(OrcaLlmConnectionSettings connection)
        {
            if (connection == null)
            {
                return false;
            }

            lock (syncRoot)
            {
                Task<OrcaModelDiscoveryResult> task;
                return activeTasks.TryGetValue(connection.id, out task) && task != null && !task.IsCompleted;
            }
        }

        private static void CompleteFinishedTasks(DeepseekTheOrcaSettings settings)
        {
            List<string> finishedIds = new List<string>();
            lock (syncRoot)
            {
                foreach (KeyValuePair<string, Task<OrcaModelDiscoveryResult>> pair in activeTasks)
                {
                    if (pair.Value != null && pair.Value.IsCompleted)
                    {
                        finishedIds.Add(pair.Key);
                    }
                }

                for (int i = 0; i < finishedIds.Count; i++)
                {
                    string id = finishedIds[i];
                    Task<OrcaModelDiscoveryResult> task = activeTasks[id];
                    activeTasks.Remove(id);
                    ApplyFinishedTask(settings, id, task);
                }
            }
        }

        private static void ApplyFinishedTask(DeepseekTheOrcaSettings settings, string connectionId, Task<OrcaModelDiscoveryResult> task)
        {
            OrcaLlmConnectionSettings connection = FindConnection(settings, connectionId);
            if (connection == null)
            {
                return;
            }

            OrcaModelDiscoveryResult result;
            try
            {
                result = task.Result;
            }
            catch (System.Exception ex)
            {
                result = OrcaModelDiscoveryResult.Failure(ex.GetType().Name + ": " + ex.Message);
            }

            connection.status = result.success ? "succeeded" : "failed";
            connection.message = result.message ?? "";
            connection.availableModels = result.models ?? new List<string>();
            RetainExistingActiveModels(connection);
            if (result.success && connection.availableModels.Count > 0)
            {
                EnsureFallbackModelSelected(settings, connection);
                LlmConnectionTester.ReportSuccessfulCall(connection.message);
                Log.Message("[Deepseek The Orca] LLM API connection '" + connection.name + "' succeeded; discovered " + connection.availableModels.Count + " model(s).");
            }
            else if (result.success)
            {
                LlmConnectionTester.ReportSuccessfulCall(connection.message);
                Log.Warning("[Deepseek The Orca] LLM API connection '" + connection.name + "' succeeded but returned no models. " + connection.message);
            }
            else if (!result.success)
            {
                LlmConnectionTester.ReportFailedCall(connection.message);
                Log.Warning("[Deepseek The Orca] LLM API connection '" + connection.name + "' failed: " + connection.message);
            }
        }

        private static void EnsureFallbackModelSelected(DeepseekTheOrcaSettings settings, OrcaLlmConnectionSettings connection)
        {
            if (settings.model.NullOrEmpty() && connection.activeModels != null && connection.activeModels.Count > 0)
            {
                settings.model = DeepseekTheOrcaSettings.MakeModelReference(connection.id, connection.activeModels[0]);
            }
        }

        private static void RetainExistingActiveModels(OrcaLlmConnectionSettings connection)
        {
            if (connection == null)
            {
                return;
            }

            if (connection.activeModels == null)
            {
                connection.activeModels = new List<string>();
            }

            if (connection.availableModels == null)
            {
                connection.availableModels = new List<string>();
            }

            connection.activeModels = connection.activeModels
                .Where(model => !model.NullOrEmpty() && connection.availableModels.Contains(model))
                .Distinct()
                .ToList();
        }

        private static OrcaLlmConnectionSettings FindConnection(DeepseekTheOrcaSettings settings, string connectionId)
        {
            if (settings == null || settings.llmConnections == null)
            {
                return null;
            }

            for (int i = 0; i < settings.llmConnections.Count; i++)
            {
                OrcaLlmConnectionSettings connection = settings.llmConnections[i];
                if (connection != null && connection.id == connectionId)
                {
                    return connection;
                }
            }

            return null;
        }
    }
}
