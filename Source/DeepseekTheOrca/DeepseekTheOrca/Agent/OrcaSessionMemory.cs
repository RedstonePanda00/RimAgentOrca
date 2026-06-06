namespace DeepseekTheOrca
{
    public static class OrcaSessionMemory
    {
        public static void Add(string source, string text)
        {
            OrcaLongTermMemoryService.Add(source, text);
        }

        public static string ContextForPrompt(string query)
        {
            return OrcaLongTermMemoryService.ContextForPrompt(query);
        }

        public static string ContextForPrompt()
        {
            return OrcaLongTermMemoryService.ContextForPrompt("");
        }

        public static void Tick()
        {
            OrcaLongTermMemoryService.Tick();
        }
    }
}
