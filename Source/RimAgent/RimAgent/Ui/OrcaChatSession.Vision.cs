using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace RimAgent;

public sealed partial class OrcaChatSession
{
    private CancellationTokenSource visualCancellation;
    private Task<LlmChatResponse> StartVisualRequest(RimAgentSettings settings, List<LlmChatMessage> messages)
    {
        try
        {
            if (Find.CurrentMap == null || string.IsNullOrWhiteSpace(settings.visionModel))
                return Task.FromResult(LlmChatResponse.Failure("Select a Vision model and load a map before requesting visual analysis."));
            var config = settings.RequestConfigForRole(OrcaLlmModelRole.Vision);
            if (config == null) return Task.FromResult(LlmChatResponse.Failure("Vision model connection is unavailable."));
            visualCancellation?.Cancel(); visualCancellation?.Dispose();
            visualCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var cancellation = visualCancellation.Token;
            var capture = OrcaVisualCapture.Capture(Find.CurrentMap, 1280, cancellation);
            return Send();
            async Task<LlmChatResponse> Send()
            {
                var image = await capture.ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                // Request-local message: do not retain image bytes in the persistent chat history.
                var copy = new List<LlmChatMessage>(messages);
                var last = copy.FindLastIndex(m => m.role == "user");
                var user = LlmChatMessage.User(last >= 0 ? copy[last].content : "Analyze the current colony view.");
                user.imageDataUrl = "data:image/jpeg;base64," + image.Data;
                if (last >= 0) copy[last] = user; else copy.Add(user);
                return await client.SendBackgroundCompletionAsync(config, copy, 1200, cancellation, OrcaLlmModelRole.Vision, 0.3f).ConfigureAwait(false);
            }
        }
        catch (Exception ex) { return Task.FromResult(LlmChatResponse.Failure(ex.Message)); }
    }
}
