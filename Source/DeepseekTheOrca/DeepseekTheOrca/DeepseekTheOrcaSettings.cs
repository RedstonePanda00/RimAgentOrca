using System.Collections.Generic;
using System;
using System.Linq;
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
        WebSearch,
        Embedding,
        Memory
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
        public List<string> activeModels = new List<string>();

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
            Scribe_Collections.Look(ref activeModels, "activeModels", LookMode.Value);
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
            if (activeModels == null)
            {
                activeModels = new List<string>();
            }

            activeModels = activeModels.Where(model => !model.NullOrEmpty()).Distinct().ToList();
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

        public bool IsModelActive(string modelId)
        {
            return !modelId.NullOrEmpty() && activeModels != null && activeModels.Contains(modelId);
        }

        public void SetModelActive(string modelId, bool active)
        {
            if (modelId.NullOrEmpty())
            {
                return;
            }

            if (activeModels == null)
            {
                activeModels = new List<string>();
            }

            activeModels.RemoveAll(value => value == modelId);
            if (active)
            {
                activeModels.Add(modelId);
            }
        }
    }

    public sealed class DeepseekTheOrcaSettings : ModSettings
    {
        public bool enableAiPlanning;
        public bool debugLogging;
        public bool enableWebSearch;
        public float colonyObservationProactiveChance = 0.25f;
        public float colonyObservationSpeakChanceMultiplier = 1f;
        public float rimtalkProactiveBaseChance = 0.15f;
        public int rimtalkProactiveForceAfterMisses = 8;
        public int rimtalkProactiveCooldownTicks = 9000;
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
        public List<string> enabledExternalSkills = new List<string>();
        public List<string> disabledExternalSkills = new List<string>();
        public List<string> enabledExtensions = new List<string>();
        public List<string> disabledExtensions = new List<string>();
        public List<string> enabledDefTools = new List<string>();
        public List<string> disabledDefTools = new List<string>();
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
        public string embeddingModel = "";
        public string memoryModel = "";
        public bool enableLongTermMemory = true;
        public int memoryMaxInjectedEntries = 5;
        public int knowledgeMaxInjectedEntries = 5;
        public float memoryMergeCosineThreshold = 0.9f;
        public bool enableSemanticMemoryQuery = true;
        public int semanticMemoryQueryWaitMs = 1500;
        public int semanticMemoryQueryHardTimeoutMs = 5000;
        public int memoryCompactionTokenThreshold = 6000;
        public int memoryChunkTokenSize = 450;
        public int memoryChunkOverlapTokens = 80;
        public bool enableSemanticToolSearch = true;
        public int toolSearchTopK = 5;
        public int toolSemanticSearchWaitMs = 1000;
        public int maxToolResultEstimatedTokens = 900;
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
            return OrcaLlmConnectionResolver.ModelForRole(this, role);
        }

        public OrcaLlmRequestConfig RequestConfigForRole(OrcaLlmModelRole role)
        {
            return OrcaLlmConnectionResolver.RequestConfigForRole(this, role);
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
                case OrcaLlmModelRole.Embedding:
                    return embeddingModel;
                case OrcaLlmModelRole.Memory:
                    return memoryModel;
                default:
                    return model;
            }
        }

        public void SetModelReferenceForRole(OrcaLlmModelRole role, string value)
        {
            switch (role)
            {
                case OrcaLlmModelRole.Controller:
                    controllerModel = value;
                    break;
                case OrcaLlmModelRole.Decision:
                    decisionModel = value;
                    break;
                case OrcaLlmModelRole.Dialogue:
                    dialogueModel = value;
                    break;
                case OrcaLlmModelRole.Tool:
                    toolModel = value;
                    break;
                case OrcaLlmModelRole.Vision:
                    visionModel = value;
                    break;
                case OrcaLlmModelRole.WebSearch:
                    webSearchModel = value;
                    break;
                case OrcaLlmModelRole.Embedding:
                    embeddingModel = value;
                    break;
                case OrcaLlmModelRole.Memory:
                    memoryModel = value;
                    break;
                default:
                    model = value;
                    break;
            }
        }

        public OrcaLlmRequestConfig RequestConfigForModelReference(string reference)
        {
            return OrcaLlmConnectionResolver.RequestConfigForModelReference(this, reference);
        }

        public List<OrcaModelOption> AvailableModelOptions()
        {
            return OrcaLlmConnectionResolver.AvailableModelOptions(this);
        }

        public bool IsExternalSkillEnabled(string skillId, bool defaultEnabled)
        {
            return IsDefToggleEnabled(skillId, defaultEnabled, enabledExternalSkills, disabledExternalSkills);
        }

        public void SetExternalSkillEnabled(string skillId, bool enabled, bool defaultEnabled)
        {
            SetDefToggleEnabled(skillId, enabled, defaultEnabled, ref enabledExternalSkills, ref disabledExternalSkills);
        }

        public bool IsExtensionEnabled(string extensionId, bool defaultEnabled)
        {
            return IsDefToggleEnabled(extensionId, defaultEnabled, enabledExtensions, disabledExtensions);
        }

        public void SetExtensionEnabled(string extensionId, bool enabled, bool defaultEnabled)
        {
            SetDefToggleEnabled(extensionId, enabled, defaultEnabled, ref enabledExtensions, ref disabledExtensions);
        }

        public bool IsDefToolEnabled(string defName, bool defaultEnabled)
        {
            return IsDefToggleEnabled(defName, defaultEnabled, enabledDefTools, disabledDefTools);
        }

        // Assigned by the mod composition root; lets the Tools layer react to
        // def tool overrides without the settings class referencing it.
        public static System.Action OnDefToolOverridesChanged;

        public void SetDefToolEnabled(string defName, bool enabled, bool defaultEnabled)
        {
            SetDefToggleEnabled(defName, enabled, defaultEnabled, ref enabledDefTools, ref disabledDefTools);
            System.Action handler = OnDefToolOverridesChanged;
            if (handler != null)
            {
                handler();
            }
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
            return OrcaLlmConnectionResolver.ModelReferenceLabel(this, reference);
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
                    availableModels = new List<string>(),
                    activeModels = new List<string>()
                };
                if (!model.NullOrEmpty())
                {
                    migrated.availableModels.Add(model);
                    migrated.activeModels.Add(model);
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
            return OrcaLlmConnectionResolver.MakeModelReference(connectionId, modelId);
        }

        public static bool TryParseModelReference(string reference, out string connectionId, out string modelId)
        {
            return OrcaLlmConnectionResolver.TryParseModelReference(reference, out connectionId, out modelId);
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableAiPlanning, "enableAiPlanning", defaultValue: false);
            Scribe_Values.Look(ref debugLogging, "debugLogging", defaultValue: false);
            Scribe_Values.Look(ref enableWebSearch, "enableWebSearch", defaultValue: false);
            Scribe_Values.Look(ref colonyObservationProactiveChance, "colonyObservationProactiveChance", 0.25f);
            Scribe_Values.Look(ref colonyObservationSpeakChanceMultiplier, "colonyObservationSpeakChanceMultiplier", 1f);
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
            Scribe_Values.Look(ref rimtalkProactiveForceAfterMisses, "rimtalkProactiveForceAfterMisses", 8);
            Scribe_Values.Look(ref rimtalkProactiveCooldownTicks, "rimtalkProactiveCooldownTicks", 9000);
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
            Scribe_Collections.Look(ref enabledExternalSkills, "enabledExternalSkills", LookMode.Value);
            Scribe_Collections.Look(ref disabledExternalSkills, "disabledExternalSkills", LookMode.Value);
            Scribe_Collections.Look(ref enabledExtensions, "enabledExtensions", LookMode.Value);
            Scribe_Collections.Look(ref disabledExtensions, "disabledExtensions", LookMode.Value);
            Scribe_Collections.Look(ref enabledDefTools, "enabledDefTools", LookMode.Value);
            Scribe_Collections.Look(ref disabledDefTools, "disabledDefTools", LookMode.Value);
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
            Scribe_Values.Look(ref embeddingModel, "embeddingModel", "");
            Scribe_Values.Look(ref memoryModel, "memoryModel", "");
            Scribe_Values.Look(ref enableLongTermMemory, "enableLongTermMemory", defaultValue: true);
            Scribe_Values.Look(ref memoryMaxInjectedEntries, "memoryMaxInjectedEntries", 5);
            Scribe_Values.Look(ref knowledgeMaxInjectedEntries, "knowledgeMaxInjectedEntries", 5);
            Scribe_Values.Look(ref memoryMergeCosineThreshold, "memoryMergeCosineThreshold", 0.9f);
            Scribe_Values.Look(ref enableSemanticMemoryQuery, "enableSemanticMemoryQuery", defaultValue: true);
            Scribe_Values.Look(ref semanticMemoryQueryWaitMs, "semanticMemoryQueryWaitMs", 1500);
            Scribe_Values.Look(ref semanticMemoryQueryHardTimeoutMs, "semanticMemoryQueryHardTimeoutMs", 5000);
            Scribe_Values.Look(ref memoryCompactionTokenThreshold, "memoryCompactionTokenThreshold", 6000);
            Scribe_Values.Look(ref memoryChunkTokenSize, "memoryChunkTokenSize", 450);
            Scribe_Values.Look(ref memoryChunkOverlapTokens, "memoryChunkOverlapTokens", 80);
            Scribe_Values.Look(ref enableSemanticToolSearch, "enableSemanticToolSearch", defaultValue: true);
            Scribe_Values.Look(ref toolSearchTopK, "toolSearchTopK", 5);
            Scribe_Values.Look(ref toolSemanticSearchWaitMs, "toolSemanticSearchWaitMs", 1000);
            Scribe_Values.Look(ref maxToolResultEstimatedTokens, "maxToolResultEstimatedTokens", 900);
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
            if (enabledExternalSkills == null)
            {
                enabledExternalSkills = new List<string>();
            }
            if (disabledExternalSkills == null)
            {
                disabledExternalSkills = new List<string>();
            }
            if (enabledExtensions == null)
            {
                enabledExtensions = new List<string>();
            }
            if (disabledExtensions == null)
            {
                disabledExtensions = new List<string>();
            }
            if (enabledDefTools == null)
            {
                enabledDefTools = new List<string>();
            }
            if (disabledDefTools == null)
            {
                disabledDefTools = new List<string>();
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
            colonyObservationSpeakChanceMultiplier = UnityEngine.Mathf.Clamp(colonyObservationSpeakChanceMultiplier, 0f, 2f);
            rimtalkProactiveBaseChance = UnityEngine.Mathf.Clamp01(rimtalkProactiveBaseChance);
            rimtalkProactiveForceAfterMisses = UnityEngine.Mathf.Clamp(rimtalkProactiveForceAfterMisses, 0, 20);
            rimtalkProactiveCooldownTicks = UnityEngine.Mathf.Clamp(rimtalkProactiveCooldownTicks, 0, 60000);
            memoryMaxInjectedEntries = UnityEngine.Mathf.Clamp(memoryMaxInjectedEntries, 1, 12);
            knowledgeMaxInjectedEntries = UnityEngine.Mathf.Clamp(knowledgeMaxInjectedEntries, 1, 12);
            memoryMergeCosineThreshold = UnityEngine.Mathf.Clamp(memoryMergeCosineThreshold, 0.75f, 0.98f);
            semanticMemoryQueryWaitMs = UnityEngine.Mathf.Clamp(semanticMemoryQueryWaitMs, 0, 5000);
            semanticMemoryQueryHardTimeoutMs = UnityEngine.Mathf.Clamp(semanticMemoryQueryHardTimeoutMs, semanticMemoryQueryWaitMs, 15000);
            memoryCompactionTokenThreshold = UnityEngine.Mathf.Clamp(memoryCompactionTokenThreshold, 1000, 20000);
            memoryChunkTokenSize = UnityEngine.Mathf.Clamp(memoryChunkTokenSize, 150, 1200);
            memoryChunkOverlapTokens = UnityEngine.Mathf.Clamp(memoryChunkOverlapTokens, 0, memoryChunkTokenSize / 2);
            toolSearchTopK = UnityEngine.Mathf.Clamp(toolSearchTopK, 1, 12);
            toolSemanticSearchWaitMs = UnityEngine.Mathf.Clamp(toolSemanticSearchWaitMs, 0, 3000);
            maxToolResultEstimatedTokens = UnityEngine.Mathf.Clamp(maxToolResultEstimatedTokens, 200, 4000);
            if (tavilySearchDepth != "basic" && tavilySearchDepth != "advanced" && tavilySearchDepth != "fast" && tavilySearchDepth != "ultra-fast")
            {
                tavilySearchDepth = "basic";
            }
        }
    }
}
