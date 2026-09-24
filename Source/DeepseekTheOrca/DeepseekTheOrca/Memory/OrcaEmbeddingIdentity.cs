using System;
using System.Security.Cryptography;
using System.Text;

namespace DeepseekTheOrca
{
    public static class OrcaEmbeddingIdentity
    {
        public static string For(OrcaLlmRequestConfig config)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.model)) return "";
            // Delimited lengths avoid collisions; API credentials are never persisted.
            var text = new StringBuilder();
            foreach (var part in new[] { config.providerId, (config.baseUrl ?? "").TrimEnd('/'), config.model, config.openAiOrganization, config.openAiProject })
            { var value = part ?? ""; text.Append(value.Length).Append(':').Append(value); }
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "");
        }

        public static string Current
        {
            get
            {
                var settings = DeepseekTheOrcaMod.Settings;
                return settings == null || !settings.HasModelForRole(OrcaLlmModelRole.Embedding)
                    ? "" : For(settings.RequestConfigForRole(OrcaLlmModelRole.Embedding));
            }
        }

        public static bool Matches(string first, string second)
        { return !string.IsNullOrEmpty(first) && string.Equals(first, second, StringComparison.Ordinal); }
    }
}
