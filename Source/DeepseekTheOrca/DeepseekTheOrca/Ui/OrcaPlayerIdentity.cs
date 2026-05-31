using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaPlayerIdentity
    {
        public static string SteamPersonaName()
        {
            string personaName = SteamUtility.SteamPersonaName;
            return personaName.NullOrEmpty() || personaName == "???" ? "Player" : personaName;
        }
    }
}
