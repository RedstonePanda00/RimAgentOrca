using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaStorytellerAppearance
    {
        public const string StorytellerDefName = "DTO_DeepseekTheOrca";

        public static void ApplyCurrent()
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            string id = settings == null ? OrcaChatPersonaManager.BuiltInOrcaId : settings.chatPersonaDefName;
            OrcaChatPersonaProfile profile = OrcaChatPersonaManager.Get(id);
            if (profile == null)
            {
                profile = OrcaChatPersonaManager.Get(OrcaChatPersonaManager.BuiltInOrcaId);
            }

            Apply(profile);
        }

        public static void Apply(OrcaChatPersonaProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            OrcaChatPersonaManager.NormalizeAppearance(profile);

            StorytellerDef def = DefDatabase<StorytellerDef>.GetNamedSilentFail(StorytellerDefName);
            if (def == null)
            {
                return;
            }

            def.label = profile.storytellerLabel;
            def.description = profile.storytellerDescription;
            def.ClearCachedData();

            string largePath = profile.storytellerPortraitLargePath;
            string tinyPath = profile.storytellerPortraitTinyPath;
            Texture2D large = ContentFinder<Texture2D>.Get(largePath, false);
            Texture2D tiny = ContentFinder<Texture2D>.Get(tinyPath, false);

            if (large != null)
            {
                def.portraitLargeTex = large;
            }
            else
            {
                LogWarningOnce("Could not find storyteller large portrait texture: " + largePath);
            }

            if (tiny != null)
            {
                def.portraitTinyTex = tiny;
            }
            else
            {
                LogWarningOnce("Could not find storyteller tiny portrait texture: " + tinyPath);
            }
        }

        public static string LargePortraitPath(OrcaChatPersonaProfile profile)
        {
            if (profile == null)
            {
                return "";
            }

            OrcaChatPersonaManager.NormalizeAppearance(profile);
            return profile.storytellerPortraitLargePath;
        }

        public static string TinyPortraitPath(OrcaChatPersonaProfile profile)
        {
            if (profile == null)
            {
                return "";
            }

            OrcaChatPersonaManager.NormalizeAppearance(profile);
            return profile.storytellerPortraitTinyPath;
        }

        private static void LogWarningOnce(string message)
        {
            Log.WarningOnce("[RimAgent] " + message, message.GetHashCode());
        }
    }
}
