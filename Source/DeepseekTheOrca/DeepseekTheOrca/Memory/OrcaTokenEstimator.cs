using System;
using System.Collections.Generic;
using System.Text;

namespace DeepseekTheOrca
{
    public static class OrcaTokenEstimator
    {
        public static int Estimate(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int tokens = 0;
            int i = 0;
            while (i < text.Length)
            {
                char ch = text[i];
                if (char.IsWhiteSpace(ch))
                {
                    i++;
                    continue;
                }

                if (IsCjk(ch))
                {
                    tokens++;
                    i++;
                    continue;
                }

                if (char.IsLetter(ch))
                {
                    int start = i;
                    while (i < text.Length && IsLatinLikeWordChar(text[i]))
                    {
                        i++;
                    }
                    tokens += Math.Max(1, (int)Math.Ceiling((i - start) / 4.0));
                    continue;
                }

                if (char.IsDigit(ch))
                {
                    int start = i;
                    while (i < text.Length && char.IsDigit(text[i]))
                    {
                        i++;
                    }
                    tokens += Math.Max(1, (int)Math.Ceiling((i - start) / 3.0));
                    continue;
                }

                tokens++;
                i++;
            }

            return Math.Max(1, tokens);
        }

        public static List<string> Chunk(string text, int chunkTokens, int overlapTokens)
        {
            List<string> chunks = new List<string>();
            text = text ?? "";
            if (text.Length == 0)
            {
                return chunks;
            }

            chunkTokens = Math.Max(1, chunkTokens);
            overlapTokens = Math.Max(0, Math.Min(overlapTokens, chunkTokens / 2));

            List<TokenSpan> spans = TokenizeSpans(text);
            if (spans.Count == 0)
            {
                chunks.Add(text);
                return chunks;
            }

            int startToken = 0;
            while (startToken < spans.Count)
            {
                int endToken = Math.Min(spans.Count, startToken + chunkTokens);
                int startChar = spans[startToken].start;
                int endChar = spans[endToken - 1].end;
                string chunk = text.Substring(startChar, endChar - startChar).Trim();
                if (chunk.Length > 0)
                {
                    chunks.Add(chunk);
                }

                if (endToken >= spans.Count)
                {
                    break;
                }

                startToken = Math.Max(startToken + 1, endToken - overlapTokens);
            }

            return chunks;
        }

        private static List<TokenSpan> TokenizeSpans(string text)
        {
            List<TokenSpan> spans = new List<TokenSpan>();
            int i = 0;
            while (i < text.Length)
            {
                char ch = text[i];
                if (char.IsWhiteSpace(ch))
                {
                    i++;
                    continue;
                }

                if (IsCjk(ch))
                {
                    spans.Add(new TokenSpan(i, i + 1));
                    i++;
                    continue;
                }

                if (char.IsLetter(ch))
                {
                    int start = i;
                    while (i < text.Length && IsLatinLikeWordChar(text[i]))
                    {
                        i++;
                    }
                    AddSubwordSpans(spans, start, i, 4);
                    continue;
                }

                if (char.IsDigit(ch))
                {
                    int start = i;
                    while (i < text.Length && char.IsDigit(text[i]))
                    {
                        i++;
                    }
                    AddSubwordSpans(spans, start, i, 3);
                    continue;
                }

                spans.Add(new TokenSpan(i, i + 1));
                i++;
            }

            return spans;
        }

        private static void AddSubwordSpans(List<TokenSpan> spans, int start, int end, int maxCharsPerToken)
        {
            int index = start;
            while (index < end)
            {
                int next = Math.Min(end, index + maxCharsPerToken);
                spans.Add(new TokenSpan(index, next));
                index = next;
            }
        }

        private static bool IsLatinLikeWordChar(char ch)
        {
            return (ch >= 'a' && ch <= 'z')
                || (ch >= 'A' && ch <= 'Z')
                || ch == '\''
                || ch == '-';
        }

        private static bool IsCjk(char ch)
        {
            return (ch >= '\u3400' && ch <= '\u4DBF')
                || (ch >= '\u4E00' && ch <= '\u9FFF')
                || (ch >= '\uF900' && ch <= '\uFAFF')
                || (ch >= '\u3040' && ch <= '\u30FF')
                || (ch >= '\uAC00' && ch <= '\uD7AF');
        }

        private struct TokenSpan
        {
            public readonly int start;
            public readonly int end;

            public TokenSpan(int start, int end)
            {
                this.start = start;
                this.end = end;
            }
        }
    }
}
