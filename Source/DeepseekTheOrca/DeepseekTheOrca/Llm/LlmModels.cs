using System.Collections.Generic;
using System.Text;

namespace DeepseekTheOrca
{
    public sealed class LlmConnectionTestResult
    {
        public bool success;
        public string message;

        public static LlmConnectionTestResult Success(string message)
        {
            return new LlmConnectionTestResult { success = true, message = message };
        }

        public static LlmConnectionTestResult Failure(string message)
        {
            return new LlmConnectionTestResult { success = false, message = message };
        }
    }

    public sealed class OrcaModelDiscoveryResult
    {
        public bool success;
        public string message;
        public List<string> models = new List<string>();

        public static OrcaModelDiscoveryResult Failure(string message)
        {
            return new OrcaModelDiscoveryResult { success = false, message = message };
        }
    }

    public sealed class LlmChatMessage
    {
        public string role;
        public string content;
        public string toolCallId;
        public List<LlmToolCall> toolCalls;

        public static LlmChatMessage System(string content)
        {
            return new LlmChatMessage { role = "system", content = content };
        }

        public static LlmChatMessage User(string content)
        {
            return new LlmChatMessage { role = "user", content = content };
        }

        public static LlmChatMessage Assistant(string content, List<LlmToolCall> toolCalls)
        {
            return new LlmChatMessage { role = "assistant", content = content, toolCalls = toolCalls };
        }

        public static LlmChatMessage Tool(string toolCallId, string content)
        {
            return new LlmChatMessage { role = "tool", toolCallId = toolCallId, content = content };
        }
    }

    public sealed class LlmToolCall
    {
        public string id;
        public string name;
        public string argumentsJson;
    }

    public sealed class LlmChatResponse
    {
        public bool success;
        public string errorMessage;
        public string content;
        public int promptTokens;
        public int completionTokens;
        public int totalTokens;
        public int cacheHitTokens;
        public int cacheMissTokens;
        public int elapsedMs;
        public string role;
        public string model;
        public string providerId;
        public readonly List<LlmToolCall> toolCalls = new List<LlmToolCall>();

        public static LlmChatResponse Success()
        {
            return new LlmChatResponse { success = true };
        }

        public static LlmChatResponse Failure(string message)
        {
            return new LlmChatResponse { success = false, errorMessage = message };
        }
    }

    public sealed class LlmStreamingChatRequest
    {
        private readonly object syncRoot = new object();
        private readonly StringBuilder rawContent = new StringBuilder();
        private readonly JsonReplyStreamExtractor replyExtractor = new JsonReplyStreamExtractor();
        private bool completed;
        private bool cancelled;
        private LlmChatResponse finalResponse;
        private string errorMessage = "";
        private string visibleText = "";
        public string role = "";
        public string model = "";
        public string providerId = "";

        public bool IsCompleted
        {
            get
            {
                lock (syncRoot)
                {
                    return completed;
                }
            }
        }

        public bool IsCancellationRequested
        {
            get
            {
                lock (syncRoot)
                {
                    return cancelled;
                }
            }
        }

        public bool Success
        {
            get
            {
                lock (syncRoot)
                {
                    return completed && finalResponse != null && finalResponse.success;
                }
            }
        }

        public string ErrorMessage
        {
            get
            {
                lock (syncRoot)
                {
                    return errorMessage;
                }
            }
        }

        public string VisibleText
        {
            get
            {
                lock (syncRoot)
                {
                    return visibleText;
                }
            }
        }

        public string RawContent
        {
            get
            {
                lock (syncRoot)
                {
                    return rawContent.ToString();
                }
            }
        }

        public LlmChatResponse FinalResponse
        {
            get
            {
                lock (syncRoot)
                {
                    return finalResponse;
                }
            }
        }

        public void AppendContent(string delta)
        {
            if (string.IsNullOrEmpty(delta))
            {
                return;
            }

            lock (syncRoot)
            {
                if (completed || cancelled)
                {
                    return;
                }

                rawContent.Append(delta);
                visibleText = JsonReplyStreamExtractor.SanitizeVisibleText(replyExtractor.Extract(rawContent.ToString()));
            }
        }

        public void Complete(LlmChatResponse response)
        {
            lock (syncRoot)
            {
                if (completed)
                {
                    return;
                }

                if (response == null)
                {
                    response = LlmChatResponse.Failure("Streaming response was empty.");
                }

                if (response.success && !string.IsNullOrEmpty(response.content))
                {
                    List<LlmToolCall> dsmlToolCalls = OrcaDsmlToolCallFallbackParser.ParseToolCalls(response.content);
                    if (dsmlToolCalls.Count > 0)
                    {
                        response.toolCalls.AddRange(dsmlToolCalls);
                        response.content = OrcaDsmlToolCallFallbackParser.StripToolCalls(response.content);
                    }
                }

                finalResponse = response;
                if (!response.success)
                {
                    errorMessage = response.errorMessage ?? "";
                }
                visibleText = JsonReplyStreamExtractor.SanitizeVisibleText(replyExtractor.Extract(rawContent.ToString()));
                completed = true;
            }
        }

        public void Fail(string message)
        {
            lock (syncRoot)
            {
                if (completed)
                {
                    return;
                }

                errorMessage = message ?? "";
                finalResponse = LlmChatResponse.Failure(errorMessage);
                finalResponse.content = rawContent.ToString();
                finalResponse.role = role;
                finalResponse.model = model;
                finalResponse.providerId = providerId;
                visibleText = JsonReplyStreamExtractor.SanitizeVisibleText(replyExtractor.Extract(rawContent.ToString()));
                completed = true;
            }
        }

        public void Cancel()
        {
            lock (syncRoot)
            {
                cancelled = true;
            }
        }
    }
}
