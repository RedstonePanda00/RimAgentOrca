using System;
using System.Linq;
using Verse;

namespace RimAgent
{
    // Capture UI/game configuration on the main thread. Credentials are resolved afresh
    // at dispatch and never serialized with a section's author/model snapshot.
    internal static class NovelTaskConfiguration
    {
        public static OrcaLlmRequestConfig Capture(NovelChapter chapter, NovelSettings options)
        {
            var settings = RimAgentMod.Settings;
            var resolved = settings == null ? null : settings.RequestConfigForRole(OrcaLlmModelRole.Dialogue);
            if (resolved == null) throw new NovelResponseException(NovelFailureKind.Configuration, "DTO_NovelNoModel".Translate());
            var connection = settings.llmConnections.FirstOrDefault(x => x != null && x.enabled && x.ActiveBaseUrl == resolved.baseUrl && x.apiKey == resolved.apiKey);
            if (connection == null) throw new NovelResponseException(NovelFailureKind.Configuration, "DTO_NovelNoModel".Translate());
            var persona = OrcaChatPersonaManager.Get(settings.chatPersonaDefName);
            chapter.author = persona.label; chapter.persona = persona.prompt;
            chapter.language = OrcaLanguageUtility.CurrentGameLanguage(); chapter.targetLength = options.targetLength;
            chapter.modelReference = OrcaLlmConnectionResolver.MakeModelReference(connection.id, resolved.model);
            chapter.modelId = resolved.model; chapter.baseUrl = resolved.baseUrl; chapter.providerId = resolved.providerId;
            chapter.organization = resolved.openAiOrganization; chapter.project = resolved.openAiProject;
            NovelWriting.CaptureWritingSkills(chapter, OrcaSkillManager.AllSkills());
            chapter.started = true;
            resolved.requestTimeoutSeconds = options.requestTimeoutSeconds;
            return resolved;
        }

        public static OrcaLlmRequestConfig Resolve(NovelChapter chapter)
        {
            string connectionId, modelId;
            OrcaLlmConnectionResolver.TryParseModelReference(chapter.modelReference, out connectionId, out modelId);
            var settings = RimAgentMod.Settings;
            var credentials = settings == null ? null : settings.llmConnections.FirstOrDefault(x => x != null && x.enabled && x.id == connectionId);
            // Never send another connection's key to this captured endpoint.
            if (credentials == null || string.IsNullOrEmpty(credentials.apiKey) || credentials.ActiveBaseUrl != chapter.baseUrl)
                throw new NovelResponseException(NovelFailureKind.Configuration, "DTO_NovelConnectionChanged".Translate());
            return new OrcaLlmRequestConfig { apiKey = credentials.apiKey, proxyUrl = credentials.proxyUrl,
                model = chapter.modelId, baseUrl = chapter.baseUrl, providerId = chapter.providerId,
                requestTimeoutSeconds = OrcaModuleStorage.Settings<NovelSettings>("RimAgent.Novel").requestTimeoutSeconds,
                openAiOrganization = chapter.organization, openAiProject = chapter.project };
        }

        public static void Copy(NovelChapter from, NovelChapter to)
        {
            to.author = from.author; to.persona = from.persona; to.language = from.language; to.targetLength = from.targetLength;
            to.modelReference = from.modelReference; to.modelId = from.modelId; to.baseUrl = from.baseUrl; to.providerId = from.providerId;
            to.organization = from.organization; to.project = from.project; to.started = from.started;
            to.writingSkillCaptured = from.writingSkillCaptured; to.writingSkillId = from.writingSkillId;
            to.writingSkillText = from.writingSkillText; to.writingSkillOrganizeText = from.writingSkillOrganizeText;
            to.writingSkillDraftText = from.writingSkillDraftText;
        }
    }
}
