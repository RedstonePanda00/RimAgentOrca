using System;

namespace DeepseekTheOrca
{
    public sealed class LlmProviderProfile
    {
        public readonly string id;
        public readonly string label;
        public readonly string defaultBaseUrl;
        public readonly string defaultModel;
        public readonly bool includeDeepseekThinkingToggle;

        public LlmProviderProfile(string id, string label, string defaultBaseUrl, string defaultModel, bool includeDeepseekThinkingToggle)
        {
            this.id = id;
            this.label = label;
            this.defaultBaseUrl = defaultBaseUrl;
            this.defaultModel = defaultModel;
            this.includeDeepseekThinkingToggle = includeDeepseekThinkingToggle;
        }
    }

    public static class LlmProviderConfig
    {
        public const string DeepSeek = "deepseek";
        public const string OpenAI = "openai";
        public const string Custom = "custom";

        private static readonly LlmProviderProfile[] profiles =
        {
            new LlmProviderProfile(DeepSeek, "DeepSeek", "https://api.deepseek.com", "deepseek-chat", true),
            new LlmProviderProfile(OpenAI, "OpenAI", "https://api.openai.com/v1", "gpt-5.5", false),
            new LlmProviderProfile(Custom, "Custom", "", "", false)
        };

        public static LlmProviderProfile Profile(string providerId)
        {
            string normalized = NormalizeProvider(providerId);
            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i].id == normalized)
                {
                    return profiles[i];
                }
            }

            return profiles[0];
        }

        public static string NormalizeProvider(string providerId)
        {
            if (providerId == OpenAI || providerId == Custom)
            {
                return providerId;
            }

            return DeepSeek;
        }

        public static string NextProvider(string providerId)
        {
            string normalized = NormalizeProvider(providerId);
            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i].id == normalized)
                {
                    return profiles[(i + 1) % profiles.Length].id;
                }
            }

            return profiles[0].id;
        }

        public static string BaseUrl(DeepseekTheOrcaSettings settings)
        {
            if (settings == null)
            {
                return profiles[0].defaultBaseUrl;
            }

            LlmProviderProfile profile = Profile(settings.apiProvider);
            string baseUrl = profile.id == Custom ? settings.customBaseUrl : profile.defaultBaseUrl;
            return NormalizeBaseUrl(baseUrl);
        }

        public static string BaseUrlFor(string providerId, string customBaseUrl)
        {
            LlmProviderProfile profile = Profile(providerId);
            string baseUrl = profile.id == Custom ? customBaseUrl : profile.defaultBaseUrl;
            return NormalizeBaseUrl(baseUrl);
        }

        public static bool IncludeDeepseekThinkingToggle(DeepseekTheOrcaSettings settings)
        {
            return settings != null && Profile(settings.apiProvider).includeDeepseekThinkingToggle;
        }

        public static bool IncludeDeepseekThinkingToggle(string providerId)
        {
            return Profile(providerId).includeDeepseekThinkingToggle;
        }

        public static string NormalizeBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return "";
            }

            string trimmed = baseUrl.Trim();
            return trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : trimmed + "/";
        }
    }
}
