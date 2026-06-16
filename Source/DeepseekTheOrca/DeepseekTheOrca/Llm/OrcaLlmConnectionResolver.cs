using System.Collections.Generic;
using Verse;

namespace DeepseekTheOrca
{
    // Resolves model references and role-to-request-config mapping from the
    // connection list persisted in DeepseekTheOrcaSettings.
    public static class OrcaLlmConnectionResolver
    {
        public static string ModelForRole(DeepseekTheOrcaSettings settings, OrcaLlmModelRole role)
        {
            OrcaLlmRequestConfig config = RequestConfigForRole(settings, role);
            if (config != null)
            {
                return config.model;
            }

            string roleModel = RoleSpecificModel(settings, role);
            if (!roleModel.NullOrEmpty())
            {
                return roleModel;
            }

            if (role == OrcaLlmModelRole.Embedding || role == OrcaLlmModelRole.Memory)
            {
                return "";
            }

            return settings.model ?? "";
        }

        public static OrcaLlmRequestConfig RequestConfigForRole(DeepseekTheOrcaSettings settings, OrcaLlmModelRole role)
        {
            settings.EnsureLlmConnections();
            string selected = settings.ModelReferenceForRole(role);
            OrcaLlmRequestConfig config = RequestConfigForModelReference(settings, selected);
            if (config != null)
            {
                return config;
            }

            if (role != OrcaLlmModelRole.Fallback && role != OrcaLlmModelRole.Embedding && role != OrcaLlmModelRole.Memory)
            {
                config = RequestConfigForModelReference(settings, settings.model);
                if (config != null)
                {
                    return config;
                }
            }

            return null;
        }

        public static OrcaLlmRequestConfig RequestConfigForModelReference(DeepseekTheOrcaSettings settings, string reference)
        {
            settings.EnsureLlmConnections();
            string connectionId;
            string modelId;
            if (!TryParseModelReference(reference, out connectionId, out modelId))
            {
                modelId = reference;
            }

            OrcaLlmConnectionSettings connection = null;
            if (!connectionId.NullOrEmpty())
            {
                connection = FindConnection(settings, connectionId);
            }

            if (connection == null && !modelId.NullOrEmpty())
            {
                connection = FindFirstEnabledConnectionContainingModel(settings, modelId);
            }

            if (connection == null)
            {
                connection = FirstUsableConnection(settings);
            }

            if (connection == null || modelId.NullOrEmpty() || connection.apiKey.NullOrEmpty() || connection.ActiveBaseUrl.NullOrEmpty())
            {
                return null;
            }

            return new OrcaLlmRequestConfig
            {
                apiKey = connection.apiKey,
                openAiOrganization = connection.openAiOrganization,
                openAiProject = connection.openAiProject,
                proxyUrl = connection.proxyUrl,
                model = modelId,
                baseUrl = connection.ActiveBaseUrl,
                providerId = connection.provider
            };
        }

        public static List<OrcaModelOption> AvailableModelOptions(DeepseekTheOrcaSettings settings)
        {
            settings.EnsureLlmConnections();
            List<OrcaModelOption> options = new List<OrcaModelOption>();
            foreach (OrcaLlmConnectionSettings connection in settings.llmConnections)
            {
                if (connection == null || !connection.enabled || connection.activeModels == null)
                {
                    continue;
                }

                for (int i = 0; i < connection.activeModels.Count; i++)
                {
                    string modelId = connection.activeModels[i];
                    if (modelId.NullOrEmpty())
                    {
                        continue;
                    }

                    string label = connection.name + " / " + modelId;
                    options.Add(new OrcaModelOption(MakeModelReference(connection.id, modelId), label, modelId, connection));
                }
            }

            return options;
        }

        public static string ModelReferenceLabel(DeepseekTheOrcaSettings settings, string reference)
        {
            OrcaLlmRequestConfig config = RequestConfigForModelReference(settings, reference);
            if (config == null)
            {
                return reference.NullOrEmpty() ? "-" : reference;
            }

            string connectionId;
            string modelId;
            if (TryParseModelReference(reference, out connectionId, out modelId))
            {
                OrcaLlmConnectionSettings connection = FindConnection(settings, connectionId);
                if (connection != null)
                {
                    return connection.name + " / " + modelId;
                }
            }

            OrcaLlmConnectionSettings byModel = FindFirstEnabledConnectionContainingModel(settings, config.model);
            return byModel == null ? config.model : byModel.name + " / " + config.model;
        }

        public static string MakeModelReference(string connectionId, string modelId)
        {
            return (connectionId ?? "") + "|" + (modelId ?? "");
        }

        public static bool TryParseModelReference(string reference, out string connectionId, out string modelId)
        {
            connectionId = "";
            modelId = "";
            if (reference.NullOrEmpty())
            {
                return false;
            }

            int separator = reference.IndexOf('|');
            if (separator < 0)
            {
                return false;
            }

            connectionId = reference.Substring(0, separator);
            modelId = reference.Substring(separator + 1);
            return !connectionId.NullOrEmpty() && !modelId.NullOrEmpty();
        }

        private static string RoleSpecificModel(DeepseekTheOrcaSettings settings, OrcaLlmModelRole role)
        {
            switch (role)
            {
                case OrcaLlmModelRole.Decision:
                    return settings.decisionModel;
                case OrcaLlmModelRole.Controller:
                    return settings.controllerModel;
                case OrcaLlmModelRole.Dialogue:
                    return settings.dialogueModel;
                case OrcaLlmModelRole.Tool:
                    return settings.toolModel;
                case OrcaLlmModelRole.Vision:
                    return settings.visionModel;
                case OrcaLlmModelRole.WebSearch:
                    return settings.webSearchModel;
                case OrcaLlmModelRole.Embedding:
                    return settings.embeddingModel;
                case OrcaLlmModelRole.Memory:
                    return settings.memoryModel;
                default:
                    return "";
            }
        }

        private static OrcaLlmConnectionSettings FindConnection(DeepseekTheOrcaSettings settings, string connectionId)
        {
            if (connectionId.NullOrEmpty() || settings.llmConnections == null)
            {
                return null;
            }

            for (int i = 0; i < settings.llmConnections.Count; i++)
            {
                if (settings.llmConnections[i] != null && settings.llmConnections[i].id == connectionId)
                {
                    return settings.llmConnections[i];
                }
            }

            return null;
        }

        private static OrcaLlmConnectionSettings FindFirstEnabledConnectionContainingModel(DeepseekTheOrcaSettings settings, string modelId)
        {
            if (modelId.NullOrEmpty() || settings.llmConnections == null)
            {
                return null;
            }

            for (int i = 0; i < settings.llmConnections.Count; i++)
            {
                OrcaLlmConnectionSettings connection = settings.llmConnections[i];
                if (connection == null || !connection.enabled || connection.availableModels == null)
                {
                    continue;
                }

                if (connection.availableModels.Contains(modelId))
                {
                    return connection;
                }
            }

            return null;
        }

        private static OrcaLlmConnectionSettings FirstUsableConnection(DeepseekTheOrcaSettings settings)
        {
            if (settings.llmConnections == null)
            {
                return null;
            }

            for (int i = 0; i < settings.llmConnections.Count; i++)
            {
                OrcaLlmConnectionSettings connection = settings.llmConnections[i];
                if (connection != null && connection.enabled && !connection.apiKey.NullOrEmpty() && !connection.ActiveBaseUrl.NullOrEmpty())
                {
                    return connection;
                }
            }

            return null;
        }
    }
}
