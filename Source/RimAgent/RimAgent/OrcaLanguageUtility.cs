using Verse;

namespace RimAgent
{
    public static class OrcaLanguageUtility
    {
        public static string CurrentGameLanguage()
        {
            try
            {
                return LanguageDatabase.activeLanguage == null || LanguageDatabase.activeLanguage.info == null
                    ? "English"
                    : LanguageDatabase.activeLanguage.info.friendlyNameNative;
            }
            catch
            {
                return "English";
            }
        }
    }
}
