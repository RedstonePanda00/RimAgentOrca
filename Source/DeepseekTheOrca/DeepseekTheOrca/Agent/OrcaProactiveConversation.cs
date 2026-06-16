using System.Collections.Generic;
using DeepseekTheOrca.Rimtalk;
using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaProactiveConversationRequest
    {
        public string source;
        public string title;
        public string body;
        public bool openChatWindow = true;

        public OrcaProactiveConversationRequest(string source, string title, string body)
        {
            this.source = source ?? "";
            this.title = title ?? "";
            this.body = body ?? "";
        }
    }

    public interface IOrcaProactiveConversationSource
    {
        void Tick();
    }

    public static class OrcaProactiveConversationManager
    {
        public const string ExtensionDefName = "DTO_Extension_AmbientProactiveDialogue";
        private const int MaxQueuedRequests = 20;
        private static readonly Queue<OrcaProactiveConversationRequest> pendingRequests = new Queue<OrcaProactiveConversationRequest>();
        private static readonly List<IOrcaProactiveConversationSource> sources = new List<IOrcaProactiveConversationSource>();
        private static bool defaultsRegistered;

        public static bool AmbientEnabled
        {
            get { return OrcaExtensionManager.ExtensionEnabled(ExtensionDefName); }
        }

        public static void RegisterSource(IOrcaProactiveConversationSource source)
        {
            EnsureDefaultsRegistered();
            if (source != null && !sources.Contains(source))
            {
                sources.Add(source);
            }
        }

        public static void Enqueue(OrcaProactiveConversationRequest request)
        {
            if (request == null)
            {
                return;
            }

            while (pendingRequests.Count >= MaxQueuedRequests)
            {
                pendingRequests.Dequeue();
            }

            pendingRequests.Enqueue(request);
        }

        public static void NotifyStorytellerIncidentScheduled(AiIncidentPlan plan, FiringIncident firingIncident, IIncidentTarget target)
        {
            OrcaNarrativeDirector.NotifyStorytellerIncidentScheduled(plan, firingIncident, target);
        }

        public static void Tick()
        {
            EnsureDefaultsRegistered();

            for (int i = 0; i < sources.Count; i++)
            {
                sources[i].Tick();
            }

            if (pendingRequests.Count == 0 || OrcaChatAgentHub.IsChatBusy)
            {
                return;
            }

            OrcaProactiveConversationRequest request = pendingRequests.Peek();
            if (OrcaChatAgentHub.TryStartProactive(request))
            {
                pendingRequests.Dequeue();
            }
        }

        private static void EnsureDefaultsRegistered()
        {
            if (defaultsRegistered)
            {
                return;
            }

            defaultsRegistered = true;
            sources.Add(new OrcaNarrativeDirectorSource());
            if (RimtalkIntegration.IsAvailable)
            {
                sources.Add(new RimtalkProactiveConversationSource());
            }
        }
    }

    public sealed class OrcaProactiveConversationExtensionWorker : OrcaExtensionWorker
    {
    }
}
