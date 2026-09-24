using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace DeepseekTheOrca
{
    // Request state is transient; successful steps and author/skill snapshots live in the book.
    internal sealed class NovelGenerationRunner
    {
        private readonly Func<NovelBook> getBook;
        private readonly Action<string> fail;
        private NovelBook Book { get { return getBook(); } }
        private NovelSettings Settings { get { return OrcaModuleStorage.Settings<NovelSettings>("RimAgent.Novel"); } }
        private Task<LlmChatResponse> request;
        private CancellationTokenSource cancellation;
        private NovelChapter pendingChapter;
        private bool drafting;
        private OrcaLlmRequestConfig config;
        public bool IsWorking { get { return request != null; } }
        public string Phase { get { return request == null ? "" : drafting ? "DTO_NovelWriting" : "DTO_NovelOrganizing"; } }
        public NovelGenerationRunner(Func<NovelBook> getBook, Action<string> fail)
        { this.getBook = getBook; this.fail = fail; }
        private void Fail(string error) { fail(error); }
        public void Resume() { config = null; }
        public void ResetForRegeneration() { config = null; pendingChapter = null; }
        public void CancelQueued() { if (cancellation != null) cancellation.Cancel(); }
        public void StartNextStep()
        {
            var chapter = Book.NextWork;
            if (chapter == null) return;
            try
            {
                // A failed organizing attempt has no successful result to preserve. Include
                // repaired opening dossiers when the player resumes that step.
                if (!chapter.frozen || string.IsNullOrEmpty(chapter.outline)) NovelWriting.Freeze(Book, chapter);
                if (!chapter.started)
                {
                    var settings = DeepseekTheOrcaMod.Settings;
                    var resolved = settings.RequestConfigForRole(OrcaLlmModelRole.Dialogue);
                    if (resolved == null) throw new InvalidOperationException("DTO_NovelNoModel".Translate());
                    var persona = OrcaChatPersonaManager.Get(settings.chatPersonaDefName);
                    chapter.author = persona.label; chapter.persona = persona.prompt;
                    chapter.language = OrcaLanguageUtility.CurrentGameLanguage(); chapter.targetLength = Settings.targetLength;
                    var connection = settings.llmConnections.FirstOrDefault(x => x != null && x.enabled && x.ActiveBaseUrl == resolved.baseUrl && x.apiKey == resolved.apiKey);
                    if (connection == null) throw new InvalidOperationException("DTO_NovelNoModel".Translate());
                    chapter.modelReference = OrcaLlmConnectionResolver.MakeModelReference(connection.id, resolved.model);
                    chapter.modelId = resolved.model; chapter.baseUrl = resolved.baseUrl; chapter.providerId = resolved.providerId;
                    chapter.organization = resolved.openAiOrganization; chapter.project = resolved.openAiProject;
                    config = resolved; chapter.started = true;
                }
                else if (config == null || pendingChapter != chapter)
                {
                    string connectionId, modelId;
                    OrcaLlmConnectionResolver.TryParseModelReference(chapter.modelReference, out connectionId, out modelId);
                    var credentials = DeepseekTheOrcaMod.Settings.llmConnections.FirstOrDefault(x => x != null && x.enabled && x.id == connectionId);
                    // Never use the resolver's fallback here: a different connection's key must
                    // not be sent to the chapter's previously captured endpoint.
                    if (credentials == null || string.IsNullOrEmpty(credentials.apiKey) || credentials.ActiveBaseUrl != chapter.baseUrl)
                        throw new InvalidOperationException("DTO_NovelConnectionChanged".Translate());
                    config = new OrcaLlmRequestConfig { apiKey = credentials.apiKey, proxyUrl = credentials.proxyUrl,
                        model = chapter.modelId, baseUrl = chapter.baseUrl, providerId = chapter.providerId,
                        openAiOrganization = chapter.organization, openAiProject = chapter.project };
                }
                if (!chapter.writingSkillCaptured)
                    NovelWriting.CaptureWritingSkills(chapter, OrcaSkillManager.AllSkills());
                drafting = !string.IsNullOrEmpty(chapter.outline);
                var messages = NovelWriting.BuildMessages(Book, chapter, drafting);
                // Output allowance scales with requested prose/selection size; never truncate input.
                int outputTokens = (int)Math.Min(int.MaxValue, drafting ? Math.Max(4096L, (long)chapter.targetLength * 3 + 2048) : Math.Max(4096L, (long)chapter.recordIds.Count * 24 + 2048));
                cancellation = new CancellationTokenSource();
                pendingChapter = chapter;
                var capturedConfig = config;
                var capturedCancellation = cancellation.Token;
                request = Task.Run(() => new LlmApiClient().SendBackgroundCompletionAsync(capturedConfig, messages, outputTokens, capturedCancellation));
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        public void PollRequest()
        {
            if (request == null || !request.IsCompleted) return;
            try
            {
                if (!request.IsCanceled)
                {
                    if (request.IsFaulted) throw request.Exception.GetBaseException();
                    NovelWriting.Accept(Book, pendingChapter, request.Result, drafting);
                    if (ReferenceEquals(pendingChapter, Book.regeneration) && pendingChapter.complete) Book.CommitRegeneration();
                }
            }
            catch (Exception ex) { Fail(ex.Message); }
            finally
            {
                request = null;
                if (cancellation != null) cancellation.Dispose(); cancellation = null;
            }
        }

    }
}
