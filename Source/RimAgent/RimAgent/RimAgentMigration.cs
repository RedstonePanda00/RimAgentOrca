using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Verse;

namespace RimAgent;

internal static class RimAgentMigration
{
    public static void Initialize(ModContentPack content)
    {
        RegisterLegacyTypeNames();
        MigrateSettings(GenFilePaths.ConfigFolderPath, content.FolderName);
    }

    internal static void RegisterLegacyTypeNames()
    {
        // GenTypes is also used when RimWorld skips its version-conversion chain.
        // Register load aliases only; new saves serialize the actual RimAgent types.
        var cache = (IDictionary)typeof(GenTypes).GetField("typeCache", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var keyType = cache.GetType().GetGenericArguments()[0];
        var constructor = keyType.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string), typeof(string) }, null);
        foreach (var type in typeof(RimAgentMod).Assembly.GetTypes())
        {
            if (type.FullName == null || !type.FullName.StartsWith("RimAgent.", StringComparison.Ordinal)) continue;
            string suffix = type.FullName["RimAgent.".Length..];
            if (suffix is "RimAgentMod" or "RimAgentSettings" or "RimAgentGameComponent")
                suffix = suffix.Replace("RimAgent", "DeepseekTheOrca");
            string legacy = "DeepseekTheOrca." + suffix;
            cache[constructor.Invoke(new object[] { legacy, null })] = type;
        }

    }

    internal static void MigrateSettings(string folder, string modFolderName)
    {
        string destination = Path.Combine(folder, GenText.SanitizeFilename($"Mod_{modFolderName}_RimAgentMod.xml"));
        if (File.Exists(destination)) return;
        foreach (string identifier in new[] { modFolderName, "DeepseekTheOrca", "RimAgent" })
        {
            string source = Path.Combine(folder, GenText.SanitizeFilename($"Mod_{identifier}_DeepseekTheOrcaMod.xml"));
            if (!File.Exists(source)) continue;
            File.Copy(source, destination, overwrite: false);
            Log.Message("[RimAgent] Migrated legacy settings; original file retained.");
            break;
        }
    }
}
