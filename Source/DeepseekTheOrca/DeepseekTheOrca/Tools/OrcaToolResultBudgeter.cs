using System.Collections.Generic;
using System.Linq;

namespace DeepseekTheOrca
{
    public static class OrcaToolResultBudgeter
    {
        public static Dictionary<string, string> Apply(Dictionary<string, string> values)
        {
            Dictionary<string, string> result = values == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(values);
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            int maxTokens = settings == null ? 900 : settings.maxToolResultEstimatedTokens;
            int estimated = Estimate(result);
            if (estimated <= maxTokens)
            {
                return result;
            }

            int budgetChars = maxTokens * 3;
            List<string> keys = result.Keys.OrderByDescending(key => result[key] == null ? 0 : result[key].Length).ToList();
            for (int i = 0; i < keys.Count && Estimate(result) > maxTokens; i++)
            {
                string key = keys[i];
                string value = result[key] ?? "";
                if (value.Length <= 80)
                {
                    continue;
                }

                int perValueBudget = System.Math.Max(80, budgetChars / System.Math.Max(1, result.Count));
                if (value.Length > perValueBudget)
                {
                    result[key] = value.Substring(0, perValueBudget) + "... [tool result truncated, original " + value.Length + " chars]";
                }
            }

            result["truncated"] = "true";
            result["estimatedTokensBeforeTruncation"] = estimated.ToString();
            return result;
        }

        private static int Estimate(Dictionary<string, string> values)
        {
            if (values == null || values.Count == 0)
            {
                return 0;
            }

            int total = 0;
            foreach (KeyValuePair<string, string> pair in values)
            {
                total += OrcaTokenEstimator.Estimate(pair.Key);
                total += OrcaTokenEstimator.Estimate(pair.Value ?? "");
            }
            return total;
        }
    }
}
