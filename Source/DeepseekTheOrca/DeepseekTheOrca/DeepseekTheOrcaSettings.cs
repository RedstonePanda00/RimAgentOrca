using System.Collections.Generic;
using System;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaLlmRequestConfig
    {
        public string apiKey;
        public string openAiOrganization;
        public string openAiProject;
        public string proxyUrl;
        public string model;
        public string baseUrl;
        public string providerId;

        public bool IncludeThinkingToggle
        {
            get { return LlmProviderConfig.IncludeDeepseekThinkingToggle(providerId); }
        }
    }

    public sealed class OrcaModelOption
    {
        public readonly string reference;
        public readonly string label;
        public readonly string modelId;
        public readonly OrcaLlmConnectionSettings connection;

        public OrcaModelOption(string reference, string label, string modelId, OrcaLlmConnectionSettings connection)
        {
            this.reference = reference;
            this.label = label;
            this.modelId = modelId;
            this.connection = connection;
        }
    }

    public enum OrcaLlmModelRole
    {
        Fallback,
        Controller,
        Decision,
        Dialogue,
        Tool,
        Vision,
        WebSearch
    }

    public sealed class OrcaHttpMcpServerSettings : IExposable
    {
        public string name = "MCP";
        public bool enabled = true;
        public string url = "";
        public string bearerToken = "";

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name", "MCP");
            Scribe_Values.Look(ref enabled, "enabled", defaultValue: true);
            Scribe_Values.Look(ref url, "url", "");
            Scribe_Values.Look(ref bearerToken, "bearerToken", "");
            if (name.NullOrEmpty())
            {
                name = "MCP";
            }
        }
    }

    public sealed class OrcaLlmConnectionSettings : IExposable
    {
        public string id = "";
        public string name = "LLM";
        public bool enabled = true;
        public string provider = LlmProviderConfig.DeepSeek;
        public string customBaseUrl = "";
        public string apiKey = "";
        public string openAiOrganization = "";
        public string openAiProject = "";
        public string proxyUrl = "";
        public string status = "notTested";
        public string message = "DTO_ConnectionNotTested";
        public List<string> availableModels = new List<string>();

        public string ActiveBaseUrl
        {
            get { return LlmProviderConfig.BaseUrlFor(provider, customBaseUrl); }
        }

        public bool CanDiscoverModels
        {
            get { return enabled && !apiKey.NullOrEmpty() && !ActiveBaseUrl.NullOrEmpty(); }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", "");
            Scribe_Values.Look(ref name, "name", "LLM");
            Scribe_Values.Look(ref enabled, "enabled", defaultValue: true);
            Scribe_Values.Look(ref provider, "provider", LlmProviderConfig.DeepSeek);
            Scribe_Values.Look(ref customBaseUrl, "customBaseUrl", "");
            Scribe_Values.Look(ref apiKey, "apiKey", "");
            Scribe_Values.Look(ref openAiOrganization, "openAiOrganization", "");
            Scribe_Values.Look(ref openAiProject, "openAiProject", "");
            Scribe_Values.Look(ref proxyUrl, "proxyUrl", "");
            Scribe_Values.Look(ref status, "status", "notTested");
            Scribe_Values.Look(ref message, "message", "DTO_ConnectionNotTested");
            Scribe_Collections.Look(ref availableModels, "availableModels", LookMode.Value);
            Normalize();
        }

        public void Normalize()
        {
            if (id.NullOrEmpty())
            {
                id = Guid.NewGuid().ToString("N");
            }

            if (name.NullOrEmpty())
            {
                name = LlmProviderConfig.Profile(provider).label;
            }

            provider = LlmProviderConfig.NormalizeProvider(provider);
            if (availableModels == null)
            {
                availableModels = new List<string>();
            }
        }

        public void MarkDirty()
        {
            status = "notTested";
            message = "DTO_ConnectionNotTested";
            if (availableModels == null)
            {
                availableModels = new List<string>();
            }
            else
            {
                availableModels.Clear();
            }
        }
    }

    public sealed class DeepseekTheOrcaSettings : ModSettings
    {
        public bool enableAiPlanning;
        public bool debugLogging;
        public bool enableWebSearch;
        public bool enableMoodPlugin = true;
        public bool enableAmbientProactiveDialogue = true;
        public float colonyObservationProactiveChance = 0.25f;
        public float rimtalkProactiveBaseChance = 0.15f;
        public float rimtalkProactiveMissBonus = 0.05f;
        public int rimtalkProactiveForceAfterMisses = 8;
        public string webSearchMode = "tavily";
        public string chatPersonaDefName = "DTO_OrcaPersona";
        public string tavilyApiKey = "";
        public int tavilyMaxResults = 3;
        public string tavilySearchDepth = "basic";
        public bool enableHttpMcp;
        public string httpMcpUrl = "";
        public string httpMcpBearerToken = "";
        public int httpMcpMaxResultChars = 6000;
        public List<OrcaHttpMcpServerSettings> httpMcpServers = new List<OrcaHttpMcpServerSettings>();
        public List<string> enabledDefSkills = new List<string>();
        public List<string> disabledDefSkills = new List<string>();
        public List<string> enabledDefPlugins = new List<string>();
        public List<string> disabledDefPlugins = new List<string>();
        public string apiProvider = LlmProviderConfig.DeepSeek;
        public string customBaseUrl = "";
        public string apiKey = "";
        public List<OrcaLlmConnectionSettings> llmConnections = new List<OrcaLlmConnectionSettings>();
        public string model = "deepseek-v4-flash";
        public string controllerModel = "";
        public string decisionModel = "";
        public string dialogueModel = "";
        public string toolModel = "";
        public string visionModel = "";
        public string webSearchModel = "";
        public int maxToolCalls = 8;
        public float planningMtbDays = 4.8f;
        public float chatWindowAlpha = 0.82f;

        public bool HasConfiguredLlm
        {
            get { return enableAiPlanning && HasModelForRole(OrcaLlmModelRole.Decision); }
        }

        public string ActiveBaseUrl
        {
            get
            {
                OrcaLlmRequestConfig config = RequestConfigForRole(OrcaLlmModelRole.Fallback);
                return config == null ? LlmProviderConfig.BaseUrl(this) : config.baseUrl;
            }
        }

        public bool UsesLocalWebSearchTool
        {
            get { return enableWebSearch && webSearchMode == "tavily"; }
        }

        public bool HasModelForRole(OrcaLlmModelRole role)
        {
            return RequestConfigForRole(role) != null;
        }

        public string ModelForRole(OrcaLlmModelRole role)
        {
            OrcaLlmRequestConfig config = RequestConfigForRole(role);
            if (config != null)
            {
                return config.model;
            }

            string roleModel = "";
            switch (role)
            {
                case OrcaLlmModelRole.Decision:
                    roleModel = decisionModel;
                    break;
                case OrcaLlmModelRole.Controller:
                    roleModel = controllerModel;
                    break;
                case OrcaLlmModelRole.Dialogue:
                    roleModel = dialogueModel;
                    break;
                case OrcaLlmModelRole.Tool:
                    roleModel = toolModel;
                    break;
                case OrcaLlmModelRole.Vision:
                    roleModel = visionModel;
                    break;
                case OrcaLlmModelRole.WebSearch:
                    roleModel = webSearchModel;
                    break;
            }

            if (!roleModel.NullOrEmpty())
            {
                return roleModel;
            }

            return model ?? "";
        }

        public OrcaLlmRequestConfig RequestConfigForRole(OrcaLlmModelRole role)
        {
            EnsureLlmConnections();
            string selected = ModelReferenceForRole(role);
            OrcaLlmRequestConfig config = RequestConfigForModelReference(selected);
            if (config != null)
            {
                return config;
            }

            if (role != OrcaLlmModelRole.Fallback)
            {
                config = RequestConfigForModelReference(model);
                if (config != null)
                {
                    return config;
                }
            }

            return null;
        }

        public string ModelReferenceForRole(OrcaLlmModelRole role)
        {
            switch (role)
            {
                case OrcaLlmModelRole.Controller:
                    return controllerModel;
                case OrcaLlmModelRole.Decision:
                    return decisionModel;
                case OrcaLlmModelRole.Dialogue:
                    return dialogueModel;
                case OrcaLlmModelRole.Tool:
                    return toolModel;
                case OrcaLlmModelRole.Vision:
                    return visionModel;
                case OrcaLlmModelRole.WebSearch:
                    return webSearchModel;
                default:
                    return model;
            }
        }

        public OrcaLlmRequestConfig RequestConfigForModelReference(string reference)
        {
            EnsureLlmConnections();
            string connectionId;
            string modelId;
            if (!TryParseModelReference(reference, out connectionId, out modelId))
            {
                modelId = reference;
            }

            OrcaLlmConnectionSettings connection = null;
            if (!connectionId.NullOrEmpty())
            {
                connection = FindConnection(connectionId);
            }

            if (connection == null && !modelId.NullOrEmpty())
            {
                connection = FindFirstEnabledConnectionContainingModel(modelId);
            }

            if (connection == null)
            {
                connection = FirstUsableConnection();
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

        public List<OrcaModelOption> AvailableModelOptions()
        {
            EnsureLlmConnections();
            List<OrcaModelOption> options = new List<OrcaModelOption>();
            foreach (OrcaLlmConnectionSettings connection in llmConnections)
            {
                if (connection == null || !connection.enabled || connection.availableModels == null)
                {
                    continue;
                }

                for (int i = 0; i < connection.availableModels.Count; i++)
                {
                    string modelId = connection.availableModels[i];
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

        public bool IsDefSkillEnabled(string defName, bool defaultEnabled)
        {
            return IsDefToggleEnabled(defName, defaultEnabled, enabledDefSkills, disabledDefSkills);
        }

        public void SetDefSkillEnabled(string defName, bool enabled, bool defaultEnabled)
        {
            SetDefToggleEnabled(defName, enabled, defaultEnabled, ref enabledDefSkills, ref disabledDefSkills);
        }

        public bool IsDefPluginEnabled(string defName, bool defaultEnabled)
        {
            return IsDefToggleEnabled(defName, defaultEnabled, enabledDefPlugins, disabledDefPlugins);
        }

        public void SetDefPluginEnabled(string defName, bool enabled, bool defaultEnabled)
        {
            SetDefToggleEnabled(defName, enabled, defaultEnabled, ref enabledDefPlugins, ref disabledDefPlugins);
        }

        private static bool IsDefToggleEnabled(string defName, bool defaultEnabled, List<string> enabledOverrides, List<string> disabledOverrides)
        {
            if (defName.NullOrEmpty())
            {
                return defaultEnabled;
            }

            if (enabledOverrides != null && enabledOverrides.Contains(defName))
            {
                return true;
            }

            if (disabledOverrides != null && disabledOverrides.Contains(defName))
            {
                return false;
            }

            return defaultEnabled;
        }

        private static void SetDefToggleEnabled(string defName, bool enabled, bool defaultEnabled, ref List<string> enabledOverrides, ref List<string> disabledOverrides)
        {
            if (defName.NullOrEmpty())
            {
                return;
            }

            if (enabledOverrides == null)
            {
                enabledOverrides = new List<string>();
            }
            if (disabledOverrides == null)
            {
                disabledOverrides = new List<string>();
            }

            enabledOverrides.RemoveAll(value => value == defName);
            disabledOverrides.RemoveAll(value => value == defName);
            if (enabled == defaultEnabled)
            {
                return;
            }

            if (enabled)
            {
                enabledOverrides.Add(defName);
            }
            else
            {
                disabledOverrides.Add(defName);
            }
        }

        public string ModelReferenceLabel(string reference)
        {
            OrcaLlmRequestConfig config = RequestConfigForModelReference(reference);
            if (config == null)
            {
                return reference.NullOrEmpty() ? "-" : reference;
            }

            string connectionId;
            string modelId;
            if (TryParseModelReference(reference, out connectionId, out modelId))
            {
                OrcaLlmConnectionSettings connection = FindConnection(connectionId);
                if (connection != null)
                {
                    return connection.name + " / " + modelId;
                }
            }

            OrcaLlmConnectionSettings byModel = FindFirstEnabledConnectionContainingModel(config.model);
            return byModel == null ? config.model : byModel.name + " / " + config.model;
        }

        public void EnsureLlmConnections()
        {
            if (llmConnections == null)
            {
                llmConnections = new List<OrcaLlmConnectionSettings>();
            }

            if (llmConnections.Count == 0 && !apiKey.NullOrEmpty())
            {
                LlmProviderProfile profile = LlmProviderConfig.Profile(apiProvider);
                OrcaLlmConnectionSettings migrated = new OrcaLlmConnectionSettings
                {
                    id = "legacy",
                    name = profile.label,
                    enabled = true,
                    provider = apiProvider,
                    customBaseUrl = customBaseUrl ?? "",
                    apiKey = apiKey ?? "",
                    availableModels = new List<string>()
                };
                if (!model.NullOrEmpty())
                {
                    migrated.availableModels.Add(model);
                }
                migrated.Normalize();
                llmConnections.Add(migrated);
            }

            for (int i = llmConnections.Count - 1; i >= 0; i--)
            {
                if (llmConnections[i] == null)
                {
                    llmConnections.RemoveAt(i);
                }
                else
                {
                    llmConnections[i].Normalize();
                }
            }
        }

        public OrcaLlmConnectionSettings AddLlmConnection()
        {
            EnsureLlmConnections();
            OrcaLlmConnectionSettings connection = new OrcaLlmConnectionSettings
            {
                id = Guid.NewGuid().ToString("N"),
                name = "LLM " + (llmConnections.Count + 1),
                enabled = true,
                provider = LlmProviderConfig.DeepSeek
            };
            connection.Normalize();
            llmConnections.Add(connection);
            return connection;
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

        private OrcaLlmConnectionSettings FindConnection(string connectionId)
        {
            if (connectionId.NullOrEmpty() || llmConnections == null)
            {
                return null;
            }

            for (int i = 0; i < llmConnections.Count; i++)
            {
                if (llmConnections[i] != null && llmConnections[i].id == connectionId)
                {
                    return llmConnections[i];
                }
            }

            return null;
        }

        private OrcaLlmConnectionSettings FindFirstEnabledConnectionContainingModel(string modelId)
        {
            if (modelId.NullOrEmpty() || llmConnections == null)
            {
                return null;
            }

            for (int i = 0; i < llmConnections.Count; i++)
            {
                OrcaLlmConnectionSettings connection = llmConnections[i];
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

        private OrcaLlmConnectionSettings FirstUsableConnection()
        {
            if (llmConnections == null)
            {
                return null;
            }

            for (int i = 0; i < llmConnections.Count; i++)
            {
                OrcaLlmConnectionSettings connection = llmConnections[i];
                if (connection != null && connection.enabled && !connection.apiKey.NullOrEmpty() && !connection.ActiveBaseUrl.NullOrEmpty())
                {
                    return connection;
                }
            }

            return null;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableAiPlanning, "enableAiPlanning", defaultValue: false);
            Scribe_Values.Look(ref debugLogging, "debugLogging", defaultValue: false);
            Scribe_Values.Look(ref enableWebSearch, "enableWebSearch", defaultValue: false);
            Scribe_Values.Look(ref enableMoodPlugin, "enableMoodPlugin", defaultValue: true);
            Scribe_Values.Look(ref enableAmbientProactiveDialogue, "enableAmbientProactiveDialogue", defaultValue: true);
            Scribe_Values.Look(ref colonyObservationProactiveChance, "colonyObservationProactiveChance", 0.25f);
            float legacyRecentLetterChance = -1f;
            float legacyColonyStateChance = -1f;
            Scribe_Values.Look(ref legacyRecentLetterChance, "recentLetterProactiveChance", -1f);
            Scribe_Values.Look(ref legacyColonyStateChance, "colonyStateProactiveChance", -1f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && colonyObservationProactiveChance == 0.25f)
            {
                if (legacyRecentLetterChance >= 0f && legacyColonyStateChance >= 0f)
                {
                    colonyObservationProactiveChance = UnityEngine.Mathf.Min(legacyRecentLetterChance, legacyColonyStateChance);
                }
                else if (legacyRecentLetterChance >= 0f)
                {
                    colonyObservationProactiveChance = legacyRecentLetterChance;
                }
                else if (legacyColonyStateChance >= 0f)
                {
                    colonyObservationProactiveChance = legacyColonyStateChance;
                }
            }
            Scribe_Values.Look(ref rimtalkProactiveBaseChance, "rimtalkProactiveBaseChance", 0.15f);
            Scribe_Values.Look(ref rimtalkProactiveMissBonus, "rimtalkProactiveMissBonus", 0.05f);
            Scribe_Values.Look(ref rimtalkProactiveForceAfterMisses, "rimtalkProactiveForceAfterMisses", 8);
            Scribe_Values.Look(ref webSearchMode, "webSearchMode", "tavily");
            Scribe_Values.Look(ref chatPersonaDefName, "chatPersonaDefName", "DTO_OrcaPersona");
            Scribe_Values.Look(ref tavilyApiKey, "tavilyApiKey", "");
            Scribe_Values.Look(ref tavilyMaxResults, "tavilyMaxResults", 3);
            Scribe_Values.Look(ref tavilySearchDepth, "tavilySearchDepth", "basic");
            Scribe_Values.Look(ref enableHttpMcp, "enableHttpMcp", defaultValue: false);
            Scribe_Values.Look(ref httpMcpUrl, "httpMcpUrl", "");
            Scribe_Values.Look(ref httpMcpBearerToken, "httpMcpBearerToken", "");
            Scribe_Values.Look(ref httpMcpMaxResultChars, "httpMcpMaxResultChars", 6000);
            Scribe_Collections.Look(ref httpMcpServers, "httpMcpServers", LookMode.Deep);
            Scribe_Collections.Look(ref enabledDefSkills, "enabledDefSkills", LookMode.Value);
            Scribe_Collections.Look(ref disabledDefSkills, "disabledDefSkills", LookMode.Value);
            Scribe_Collections.Look(ref enabledDefPlugins, "enabledDefPlugins", LookMode.Value);
            Scribe_Collections.Look(ref disabledDefPlugins, "disabledDefPlugins", LookMode.Value);
            Scribe_Values.Look(ref apiProvider, "apiProvider", LlmProviderConfig.DeepSeek);
            Scribe_Values.Look(ref customBaseUrl, "customBaseUrl", "");
            Scribe_Values.Look(ref apiKey, "apiKey", "");
            Scribe_Collections.Look(ref llmConnections, "llmConnections", LookMode.Deep);
            Scribe_Values.Look(ref model, "model", "deepseek-v4-flash");
            Scribe_Values.Look(ref controllerModel, "controllerModel", "");
            Scribe_Values.Look(ref decisionModel, "decisionModel", "");
            Scribe_Values.Look(ref dialogueModel, "dialogueModel", "");
            Scribe_Values.Look(ref toolModel, "toolModel", "");
            Scribe_Values.Look(ref visionModel, "visionModel", "");
            Scribe_Values.Look(ref webSearchModel, "webSearchModel", "");
            Scribe_Values.Look(ref maxToolCalls, "maxToolCalls", 8);
            Scribe_Values.Look(ref planningMtbDays, "planningMtbDays", 4.8f);
            Scribe_Values.Look(ref chatWindowAlpha, "chatWindowAlpha", 0.82f);
            chatWindowAlpha = UnityEngine.Mathf.Clamp01(chatWindowAlpha);
            if (webSearchMode.NullOrEmpty())
            {
                webSearchMode = "tavily";
            }
            if (chatPersonaDefName.NullOrEmpty())
            {
                chatPersonaDefName = OrcaChatPersonaManager.BuiltInOrcaId;
            }

            if (httpMcpServers == null)
            {
                httpMcpServers = new List<OrcaHttpMcpServerSettings>();
            }
            if (enabledDefSkills == null)
            {
                enabledDefSkills = new List<string>();
            }
            if (disabledDefSkills == null)
            {
                disabledDefSkills = new List<string>();
            }
            if (enabledDefPlugins == null)
            {
                enabledDefPlugins = new List<string>();
            }
            if (disabledDefPlugins == null)
            {
                disabledDefPlugins = new List<string>();
            }

            if (httpMcpServers.Count == 0 && !httpMcpUrl.NullOrEmpty())
            {
                httpMcpServers.Add(new OrcaHttpMcpServerSettings
                {
                    name = "MCP",
                    enabled = true,
                    url = httpMcpUrl,
                    bearerToken = httpMcpBearerToken ?? ""
                });
            }

            for (int i = httpMcpServers.Count - 1; i >= 0; i--)
            {
                if (httpMcpServers[i] == null)
                {
                    httpMcpServers.RemoveAt(i);
                }
                else if (httpMcpServers[i].name.NullOrEmpty())
                {
                    httpMcpServers[i].name = "MCP " + (i + 1);
                }
            }

            apiProvider = LlmProviderConfig.NormalizeProvider(apiProvider);
            EnsureLlmConnections();

            tavilyMaxResults = UnityEngine.Mathf.Clamp(tavilyMaxResults, 1, 10);
            httpMcpMaxResultChars = UnityEngine.Mathf.Clamp(httpMcpMaxResultChars, 500, 20000);
            colonyObservationProactiveChance = UnityEngine.Mathf.Clamp01(colonyObservationProactiveChance);
            rimtalkProactiveBaseChance = UnityEngine.Mathf.Clamp01(rimtalkProactiveBaseChance);
            rimtalkProactiveMissBonus = UnityEngine.Mathf.Clamp01(rimtalkProactiveMissBonus);
            rimtalkProactiveForceAfterMisses = UnityEngine.Mathf.Clamp(rimtalkProactiveForceAfterMisses, 1, 20);
            if (tavilySearchDepth != "basic" && tavilySearchDepth != "advanced" && tavilySearchDepth != "fast" && tavilySearchDepth != "ultra-fast")
            {
                tavilySearchDepth = "basic";
            }
        }
    }
}
