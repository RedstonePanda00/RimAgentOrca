using System.Collections.Generic;
using System.Threading.Tasks;

namespace DeepseekTheOrca
{
    public sealed class OrcaEmbeddingResult
    {
        public bool success;
        public string errorMessage = "";
        public List<float> embedding = new List<float>();

        public static OrcaEmbeddingResult Failure(string message)
        {
            return new OrcaEmbeddingResult { success = false, errorMessage = message ?? "" };
        }
    }

    public sealed class OrcaEmbeddingClient
    {
        private readonly LlmApiClient client = new LlmApiClient();

        public Task<OrcaEmbeddingResult> EmbedAsync(DeepseekTheOrcaSettings settings, string text)
        {
            return client.SendEmbeddingAsync(settings, text);
        }

        public Task<OrcaEmbeddingResult> EmbedAsync(DeepseekTheOrcaSettings settings, string text, int timeoutMs)
        {
            return client.SendEmbeddingAsync(settings, text, timeoutMs);
        }
    }
}
