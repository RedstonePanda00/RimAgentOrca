using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaSettingsFormatters
    {
        public static string ConnectionStatusText(LlmConnectionStatus status)
        {
            switch (status)
            {
                case LlmConnectionStatus.Testing:
                    return "DTO_ConnectionStatusTesting".Translate();
                case LlmConnectionStatus.Succeeded:
                    return "DTO_ConnectionStatusSucceeded".Translate();
                case LlmConnectionStatus.Failed:
                    return "DTO_ConnectionStatusFailed".Translate();
                default:
                    return "DTO_ConnectionStatusNotTested".Translate();
            }
        }

        public static string ConnectionStatusText(string status)
        {
            switch (status)
            {
                case "testing":
                    return "DTO_ConnectionStatusTesting".Translate();
                case "succeeded":
                    return "DTO_ConnectionStatusSucceeded".Translate();
                case "failed":
                    return "DTO_ConnectionStatusFailed".Translate();
                default:
                    return "DTO_ConnectionStatusNotTested".Translate();
            }
        }

        public static string TranslateIfKey(string text)
        {
            if (text == "DTO_ConnectionNotTested" || text == "DTO_ConnectionTesting")
            {
                return text.Translate();
            }

            return text ?? "";
        }
    }
}
