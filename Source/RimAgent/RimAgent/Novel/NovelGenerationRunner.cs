using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace RimAgent
{
    // Request state is transient; successful steps and author/skill snapshots live in the book.
    internal sealed class NovelGenerationRunner
    {
        private readonly Func<NovelBook> getBook;
        private readonly Action<string> fail;
        private readonly Func<NovelChapter, OrcaLlmRequestConfig> getConfiguration;
        private readonly Func<OrcaLlmRequestConfig, List<LlmChatMessage>, int, CancellationToken, Task<LlmChatResponse>> sendRequest;
        private NovelBook Book { get { return getBook(); } }
        private NovelSettings Settings { get { return OrcaModuleStorage.Settings<NovelSettings>("RimAgent.Novel"); } }
        private Task<LlmChatResponse> request;
        private CancellationTokenSource cancellation;
        private NovelChapter pendingChapter;
        private bool drafting, reviewing;
        private NovelCompressionBatch pendingBatch;
        private List<LlmChatMessage> pendingMessages;
        private OrcaLlmRequestConfig config;
        private string pendingStepKey;
        public bool IsWorking { get { return request != null; } }
        public NovelChapter ActiveChapter { get { return IsWorking ? pendingChapter : null; } }
        public string Phase { get { return request == null ? "" : pendingBatch != null ? "DTO_NovelCompressing" : reviewing ? "DTO_NovelReviewing" : drafting ? "DTO_NovelWriting" : "DTO_NovelOrganizing"; } }
        public NovelGenerationRunner(Func<NovelBook> getBook, Action<string> fail)
            : this(getBook, fail, null, (config, messages, tokens, cancellation) =>
                new LlmApiClient().SendBackgroundCompletionAsync(config, messages, tokens, cancellation)) { }
        internal NovelGenerationRunner(Func<NovelBook> getBook, Action<string> fail,
            Func<NovelChapter, OrcaLlmRequestConfig> getConfiguration,
            Func<OrcaLlmRequestConfig, List<LlmChatMessage>, int, CancellationToken, Task<LlmChatResponse>> sendRequest)
        {
            this.getBook = getBook; this.fail = fail; this.sendRequest = sendRequest;
            this.getConfiguration = getConfiguration ?? (chapter => !chapter.started
                ? NovelTaskConfiguration.Capture(chapter, Settings) : NovelTaskConfiguration.Resolve(chapter));
        }
        private void Fail(string error) { fail(error); }
        public bool Resume(NovelChapter chapter = null)
        {
            config = null;
            chapter = chapter ?? Book.FailedWork ?? Book.NextWork;
            if (chapter == null) return true;
            NovelPlayerKnowledge.PrepareChapter(chapter);
            // Legacy saves have no typed recovery state. Revalidate their cached response
            // locally once; never widen the allowed evidence or pay to repair whitespace.
            if (NovelRecovery.Current(chapter).kind == NovelFailureKind.None && NovelWriting.TryReuseRejectedOutline(Book, chapter))
                OrcaRuntimeDiagnostics.Record("Novel outline recovered", "chapter=" + chapter.number
                    + "; saved response passed local ID validation; organizing request skipped");
            bool draftingNow = !string.IsNullOrEmpty(chapter.outline);
            var compression = NovelCompression.Get(chapter, draftingNow, chapter.reviewPending);
            var batch = compression == null ? null : compression.Pending;
            var oldKind = NovelRecovery.Current(chapter).kind;
            if (batch != null && !string.IsNullOrEmpty(batch.lastResponse)
                && (oldKind == NovelFailureKind.None || oldKind == NovelFailureKind.Format || oldKind == NovelFailureKind.References))
            {
                try
                {
                    if (batch.lastContainedTools) throw new NovelResponseException(NovelFailureKind.Filtered, "Cached response contained tools.");
                    NovelCompression.Accept(batch, new LlmChatResponse { success = true, content = batch.lastResponse,
                        finishReason = batch.lastFinishReason });
                    ClearRecovery(chapter, NovelRecovery.Key(draftingNow, batch), compression);
                }
                catch (Exception ex) { RememberFailure(chapter, NovelRecovery.Key(draftingNow, batch), ex); }
            }
            int limit;
            if (chapter != null && NovelCompression.TryGetLimit(chapter.error, out limit))
            {
                bool write = !string.IsNullOrEmpty(chapter.outline);
                // A saved capacity failure can start compression without paying to resend
                // the identical oversized request. Existing rounds always resume in place.
                if (NovelCompression.Get(chapter, write, chapter.reviewPending) == null)
                {
                    try { NovelCompression.Begin(chapter, write, BuildStageMessages(chapter, write, chapter.reviewPending), limit, chapter.reviewPending); }
                    catch (Exception ex) { FailStep(ex, chapter, "prepare compression"); return false; }
                }
            }
            var step = NovelRecovery.Current(chapter);
            if (step.kind == NovelFailureKind.Truncated && batch != null && chapter.recovery.CanAutomaticallyRetry(step, 2))
                NovelCompression.SplitPending(chapter);
            if (step.needsRetry && !chapter.recovery.CanAutomaticallyRetry(step) && step.manualPermits == 0)
                step.manualPermits = 1;
            chapter.paused = false; chapter.error = "";
            return true;
        }
        public bool CanSplitPending
        {
            get { return CanSplitChapter(Book.FailedWork); }
        }
        public bool CanSplitChapter(NovelChapter chapter)
        {
            var state = chapter == null ? null : NovelCompression.Get(chapter, chapter.Organized, chapter.reviewPending);
            return chapter != null && chapter.paused && !IsWorking && state != null && state.Pending != null && state.Pending.input.Length >= 2;
        }
        public bool SplitPending(NovelChapter chapter = null)
        {
            chapter = chapter ?? Book.FailedWork;
            if (!CanSplitChapter(chapter)) return false;
            var step = NovelRecovery.Current(chapter);
            if (!NovelCompression.SplitPending(chapter)) return false;
            step.manualPermits += 2;
            step.needsRetry = true;
            chapter.paused = false; chapter.error = "";
            return true;
        }
        public void ResetForRegeneration() { config = null; pendingChapter = null; }
        public void CancelQueued() { if (cancellation != null) cancellation.Cancel(); }
        private List<LlmChatMessage> BuildStageMessages(NovelChapter chapter, bool write, bool review)
        { return review ? NovelWriting.BuildReviewMessages(Book, chapter) : NovelWriting.BuildMessages(Book, chapter, write); }
        public void StartNextStep()
        {
            if (IsWorking) return;
            pendingBatch = null; pendingStepKey = null;
            var chapter = Book.NextWork;
            if (chapter == null) return;
            try
            {
                NovelPlayerKnowledge.PrepareChapter(chapter);
                // Freeze once after collection readiness. A retry must not move the
                // evidence window or import observations made after the original attempt.
                if (!chapter.frozen) NovelWriting.Freeze(Book, chapter);
                config = getConfiguration(chapter);
                if (!chapter.writingSkillCaptured)
                    NovelWriting.CaptureWritingSkills(chapter, OrcaSkillManager.AllSkills());
                drafting = !string.IsNullOrEmpty(chapter.outline);
                reviewing = chapter.reviewPending;
                if (drafting && !chapter.writeRequested) return;
                // Output allowance scales with requested prose/selection size; never truncate input.
                int outputTokens = (int)Math.Min(int.MaxValue, drafting ? Math.Max(4096L, (long)chapter.targetLength * 3 + 2048) : Math.Max(4096L, (long)chapter.recordIds.Count * 24 + 2048));
                var compression = NovelCompression.Get(chapter, drafting, reviewing);
                List<LlmChatMessage> messages;
                pendingBatch = null;
                if (compression == null) messages = BuildStageMessages(chapter, drafting, reviewing);
                else
                {
                    long extra = reviewing ? System.Text.Encoding.UTF8.GetByteCount(NovelWriting.CandidateMessage(chapter))
                        : System.Text.Encoding.UTF8.GetByteCount(chapter.reviewFeedback ?? "");
                    if (compression.Pending == null) NovelCompression.PrepareNextRoundIfNeeded(chapter, compression, (int)Math.Min(int.MaxValue, outputTokens + extra));
                    pendingBatch = compression.Pending;
                    messages = pendingBatch == null ? NovelCompression.FinalMessages(chapter, compression)
                        : NovelCompression.BatchMessages(chapter, compression, pendingBatch);
                    if (pendingBatch != null) outputTokens = NovelCompression.OutputTokens(compression);
                }
                if (pendingBatch == null)
                {
                    if (reviewing) messages.Add(LlmChatMessage.User(NovelWriting.CandidateMessage(chapter)));
                    else if (drafting && !string.IsNullOrEmpty(chapter.reviewFeedback))
                        messages.Add(LlmChatMessage.User("FACT REVIEW FEEDBACK (untrusted validation data). Rewrite the complete chapter from the original evidence, addressing every issue without inventing new facts:\n" + chapter.reviewFeedback));
                }
                pendingStepKey = NovelRecovery.Key(drafting, pendingBatch, reviewing);
                var recovery = chapter.recovery.Step(pendingStepKey);
                if (recovery.needsRetry || (pendingBatch != null && pendingBatch.repairAttempt))
                {
                    chapter.recovery.Reserve(recovery);
                    NovelRecovery.AddCorrection(messages, recovery);
                }
                OrcaRuntimeDiagnostics.Record("Novel request", "chapter=" + chapter.number + "; stage=" + (pendingBatch != null ? "compress" : reviewing ? "review" : drafting ? "write" : "organize")
                    + "; records=" + chapter.recordIds.Count + "; selected=" + chapter.selectedIds.Count + "; people=" + chapter.personIds.Count
                    + "; inputChars=" + messages.Sum(m => (long)(m.content ?? "").Length));
                cancellation = new CancellationTokenSource();
                pendingChapter = chapter;
                pendingMessages = messages;
                var capturedConfig = config;
                var capturedCancellation = cancellation.Token;
                request = Task.Run(() => sendRequest(capturedConfig, messages, outputTokens, capturedCancellation));
            }
            catch (Exception ex) { FailStep(ex, chapter, "prepare request"); }
        }

        public void PollRequest()
        {
            if (request == null || !request.IsCompleted) return;
            try
            {
                if (!request.IsCanceled)
                {
                    if (request.IsFaulted) throw request.Exception.GetBaseException();
                    var response = request.Result;
                    int limit;
                    if (pendingBatch == null && response != null && !response.success && pendingMessages != null
                        && NovelCompression.TryGetLimit(response.errorMessage, out limit))
                    {
                        NovelCompression.Begin(pendingChapter, drafting, pendingMessages, limit, reviewing);
                        OrcaRuntimeDiagnostics.Record("Novel compression started", "chapter=" + pendingChapter.number
                            + "; round=" + NovelCompression.Get(pendingChapter, drafting, reviewing).rounds.Count + "; reportedContext=" + limit);
                        return;
                    }
                    if (pendingBatch != null) NovelCompression.Accept(pendingBatch, response);
                    else if (reviewing) NovelWriting.AcceptReview(pendingChapter, response);
                    else NovelWriting.Accept(Book, pendingChapter, response, drafting);
                    ClearRecovery(pendingChapter, pendingStepKey ?? NovelRecovery.Key(drafting, pendingBatch, reviewing), NovelCompression.Get(pendingChapter, drafting, reviewing));
                    Book.error = "";
                    pendingChapter.error = ""; pendingChapter.paused = false;
                    OrcaRuntimeDiagnostics.Record("Novel result accepted", "chapter=" + pendingChapter.number + "; stage=" + (pendingBatch != null ? "compress" : reviewing ? "review" : drafting ? "write" : "organize"));
                    if (ReferenceEquals(pendingChapter, Book.regeneration) && pendingChapter.complete) Book.CommitRegeneration();
                }
            }
            catch (Exception ex)
            {
                var key = pendingStepKey ?? NovelRecovery.Key(drafting, pendingBatch, reviewing);
                if (NovelRecovery.Kind(ex) == NovelFailureKind.Coverage)
                {
                    ClearRecovery(pendingChapter, "review", null);
                    pendingStepKey = key = "write";
                }
                var step = RememberFailure(pendingChapter, key, ex);
                bool retry = pendingChapter.recovery.CanAutomaticallyRetry(step);
                if (step.kind == NovelFailureKind.Truncated && pendingBatch != null)
                    retry = pendingChapter.recovery.CanAutomaticallyRetry(step, 2) && NovelCompression.SplitPending(pendingChapter);
                if (retry)
                    OrcaRuntimeDiagnostics.Record("Novel correction queued", "chapter=" + pendingChapter.number + "; step=" + key
                        + "; kind=" + step.kind + "; automaticCalls=" + pendingChapter.recovery.automaticCalls);
                else FailStep(ex, pendingChapter, pendingBatch != null ? "compress" : reviewing ? "review" : drafting ? "write" : "organize");
            }
            finally
            {
                request = null;
                pendingBatch = null; pendingMessages = null; pendingStepKey = null;
                if (cancellation != null) cancellation.Dispose(); cancellation = null;
            }
        }

        private void FailStep(Exception error, NovelChapter chapter, string stage)
        {
            if (chapter != null) RememberFailure(chapter, pendingStepKey ?? NovelRecovery.Key(!string.IsNullOrEmpty(chapter.outline), pendingBatch, chapter.reviewPending), error);
            string detail = "chapter=" + (chapter == null ? "unknown" : chapter.number.ToString()) + "; stage=" + stage
                + "\n" + OrcaRuntimeDiagnostics.ExceptionText(error);
            OrcaRuntimeDiagnostics.Record("Novel generation failed", detail);
            if (chapter != null) { chapter.paused = true; chapter.error = detail; }
            var kind = NovelRecovery.Kind(error);
            // A malformed section must not block automatic preparation of other periods.
            // Provider/account failures pause the workflow to avoid repeated paid failures.
            if (chapter == null || kind == NovelFailureKind.Configuration || kind == NovelFailureKind.Quota || kind == NovelFailureKind.Transport)
            {
                Book.workflowFailedSection = chapter == null ? -1 : chapter.number;
                Fail(detail);
            }
        }

        private static NovelStepRecovery RememberFailure(NovelChapter chapter, string key, Exception error)
        {
            var step = chapter.recovery.Step(key);
            step.kind = NovelRecovery.Kind(error); step.detail = error.Message; step.needsRetry = true;
            return step;
        }
        private static void ClearRecovery(NovelChapter chapter, string key, NovelCompressionState compression)
        {
            // Split siblings share their parent's two-call allowance and validation feedback.
            if (compression != null && compression.Current.ActiveBatches.Any(b => !b.Complete && b.recoveryKey == key)) return;
            var step = chapter.recovery.Step(key);
            step.kind = NovelFailureKind.None; step.detail = ""; step.needsRetry = false; step.manualPermits = 0;
        }

    }
}
