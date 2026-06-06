using System.Collections.Generic;
using System.Linq;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaKnowledgeRetriever
    {
        public static List<OrcaKnowledgeEntry> Retrieve(List<OrcaKnowledgeEntry> entries, string query, int maxCount)
        {
            if (entries == null || entries.Count == 0 || query.NullOrEmpty())
            {
                return new List<OrcaKnowledgeEntry>();
            }

            string lower = query.ToLowerInvariant();
            return entries.Select(entry => new ScoredEntry(entry, Score(entry, lower)))
                .Where(item => item.score > 0)
                .OrderByDescending(item => item.score)
                .ThenByDescending(item => item.entry.priority)
                .Take(maxCount)
                .Select(item => item.entry)
                .ToList();
        }

        private static int Score(OrcaKnowledgeEntry entry, string lowerQuery)
        {
            int score = 0;
            if (entry == null)
            {
                return 0;
            }

            AddScore(ref score, lowerQuery, entry.id, 40);
            AddScore(ref score, lowerQuery, entry.label, 40);
            foreach (string alias in entry.aliases ?? new List<string>())
            {
                AddScore(ref score, lowerQuery, alias, 55);
            }
            foreach (string category in entry.categories ?? new List<string>())
            {
                AddScore(ref score, lowerQuery, category, 8);
            }

            if (!entry.text.NullOrEmpty())
            {
                foreach (string token in lowerQuery.Split(' ', '\n', '\r', '\t', ',', '.', ';', ':'))
                {
                    string clean = token.Trim();
                    if (clean.Length >= 4 && entry.text.ToLowerInvariant().Contains(clean))
                    {
                        score += 2;
                    }
                }
            }

            return score + entry.priority;
        }

        private static void AddScore(ref int score, string lowerQuery, string value, int weight)
        {
            if (value.NullOrEmpty())
            {
                return;
            }

            string lowerValue = value.ToLowerInvariant();
            if (lowerQuery.Contains(lowerValue))
            {
                score += weight;
            }
        }

        private sealed class ScoredEntry
        {
            public readonly OrcaKnowledgeEntry entry;
            public readonly int score;

            public ScoredEntry(OrcaKnowledgeEntry entry, int score)
            {
                this.entry = entry;
                this.score = score;
            }
        }
    }
}
