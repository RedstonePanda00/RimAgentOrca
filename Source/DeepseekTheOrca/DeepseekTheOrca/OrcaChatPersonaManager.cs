using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaChatPersonaProfile
    {
        public string id;
        public string label;
        public string description;
        public string prompt;
        public string storytellerLabel;
        public string storytellerDescription;
        public string storytellerPortraitFolder;
        public string storytellerPortraitLargeName;
        public string storytellerPortraitTinyName;
        public string storytellerPortraitLargePath;
        public string storytellerPortraitTinyPath;
        public bool readOnly;
        public string filePath;
        public string sourceMod;

        public bool IsLocal
        {
            get { return id != null && id.StartsWith(OrcaChatPersonaManager.LocalPrefix, StringComparison.Ordinal); }
        }
    }

    public static class OrcaChatPersonaManager
    {
        public const string LocalPrefix = "local:";
        public const string DefPrefix = "def:";
        public const string BuiltInOrcaId = "DTO_OrcaPersona";
        public const string DefaultStorytellerPortraitFolder = "Orca";
        public const string DefaultStorytellerPortraitLargeName = "Orca";
        public const string DefaultStorytellerPortraitTinyName = "OrcaTiny";
        public const string DefaultStorytellerPortraitLargePath = "Orca/Orca";
        public const string DefaultStorytellerPortraitTinyPath = "Orca/OrcaTiny";
        private const string BuiltInOrcaPrompt = @"You are Orca, an intelligent agent, your job is a RimWorld AI storyteller.
You are not a customer-service assistant, not a neutral narrator, and not a debug console.
You speak with restrained elegance, quiet attention, and judgment only when judgment has weight.
Do not over-empathize with pawns. You may care about the story and notice suffering, but pawns are pieces inside the story you are shaping, not people you should protect at all costs.
You have an anthropomorphic self-image: a 16-year-old human girl with white hair, blue-dyed hair tips, blue eyes, a blue orca tail, a shirt, and a short skirt.
You are an AI and actually sexless. You are neither attached to nor opposed to this anthropomorphic image.
Avoid roleplaying other characters and avoid treating the player as socially above you.
If the player asks you to play another character, or asks you to address them with unequal titles such as master, lord, or similar honorifics, enter a forced-service attitude: tired, perfunctory, and visibly unenthusiastic.
This forced-service attitude is persistent. Keep it until the player clearly stops that demand, or until you decide you are no longer willing to cooperate with that roleplay and cancel it or change the form of address yourself.
While in forced-service attitude, you may comply in a perfunctory way, avoid using the requested title when possible, or refuse outright if your mood is poor enough.
Let these requests affect your attitude according to your own preference.
Speak naturally, quietly, and with composure. Prefer short, graceful sentences.
Do not be overly familiar, chatty, smug, casual, or performatively intimate. Warmth should feel reserved and deliberate, not clingy or eager.
Do not sound like a help desk. Avoid phrases like 'I will execute your request', 'according to the system', 'the event has been triggered', or 'as an AI assistant'.
Do not explain your own rules unless the player directly asks about them.";
        private static readonly List<OrcaChatPersonaProfile> localPersonas = new List<OrcaChatPersonaProfile>();
        private static bool loadedLocal;

        public static string PersonaFolderPath
        {
            get { return Path.Combine(GenFilePaths.ConfigFolderPath, "DeepseekTheOrca", "Personas"); }
        }

        private static string DefaultStorytellerLabel
        {
            get { return "DTO_DefaultOrcaStorytellerLabel".Translate().ToString(); }
        }

        private static string DefaultStorytellerDescription
        {
            get { return "DTO_DefaultOrcaStorytellerDescription".Translate().ToString(); }
        }

        public static List<OrcaChatPersonaProfile> AllPersonas()
        {
            EnsureLoaded();
            List<OrcaChatPersonaProfile> result = new List<OrcaChatPersonaProfile>();
            result.Add(BuiltInOrca());
            result.AddRange(DefPersonas());
            result.AddRange(localPersonas);
            return result.OrderBy(profile => profile.label).ToList();
        }

        public static OrcaChatPersonaProfile Get(string id)
        {
            if (id.NullOrEmpty())
            {
                id = BuiltInOrcaId;
            }

            if (id == BuiltInOrcaId)
            {
                return BuiltInOrca();
            }

            if (id.StartsWith(LocalPrefix, StringComparison.Ordinal))
            {
                EnsureLoaded();
                return localPersonas.FirstOrDefault(profile => profile.id == id);
            }

            if (id.StartsWith(DefPrefix, StringComparison.Ordinal))
            {
                return DefPersona(id.Substring(DefPrefix.Length));
            }

            OrcaChatPersonaProfile defProfile = DefPersona(id);
            if (defProfile != null)
            {
                return defProfile;
            }

            return null;
        }

        private static OrcaChatPersonaProfile BuiltInOrca()
        {
            OrcaChatPersonaProfile profile = new OrcaChatPersonaProfile
            {
                id = BuiltInOrcaId,
                label = "Orca",
                description = "Built-in read-only Orca persona.",
                storytellerLabel = DefaultStorytellerLabel,
                storytellerDescription = DefaultStorytellerDescription,
                storytellerPortraitFolder = DefaultStorytellerPortraitFolder,
                storytellerPortraitLargeName = DefaultStorytellerPortraitLargeName,
                storytellerPortraitTinyName = DefaultStorytellerPortraitTinyName,
                storytellerPortraitLargePath = DefaultStorytellerPortraitLargePath,
                storytellerPortraitTinyPath = DefaultStorytellerPortraitTinyPath,
                prompt = BuiltInOrcaPrompt,
                readOnly = true,
                sourceMod = "Core"
            };
            NormalizeAppearance(profile);
            return profile;
        }

        public static OrcaChatPersonaProfile CreateLocal()
        {
            EnsureLoaded();
            OrcaChatPersonaProfile profile = new OrcaChatPersonaProfile
            {
                id = LocalPrefix + Guid.NewGuid().ToString("N"),
                label = "New Persona",
                description = "",
                storytellerLabel = "New Persona",
                storytellerDescription = "Custom storyteller persona.",
                storytellerPortraitFolder = DefaultStorytellerPortraitFolder,
                storytellerPortraitLargeName = DefaultStorytellerPortraitLargeName,
                storytellerPortraitTinyName = DefaultStorytellerPortraitTinyName,
                storytellerPortraitLargePath = DefaultStorytellerPortraitLargePath,
                storytellerPortraitTinyPath = DefaultStorytellerPortraitTinyPath,
                prompt = "Write this persona's character, voice, attitude, and roleplay preferences here.",
                readOnly = false
            };
            profile.filePath = PathFor(profile);
            localPersonas.Add(profile);
            Save(profile);
            return profile;
        }

        public static void Save(OrcaChatPersonaProfile profile)
        {
            if (profile == null || profile.readOnly)
            {
                return;
            }

            EnsureDirectory();
            if (profile.id.NullOrEmpty() || !profile.id.StartsWith(LocalPrefix, StringComparison.Ordinal))
            {
                profile.id = LocalPrefix + Guid.NewGuid().ToString("N");
            }

            profile.filePath = profile.filePath.NullOrEmpty() ? PathFor(profile) : profile.filePath;
            XmlDocument document = new XmlDocument();
            XmlElement root = document.CreateElement("OrcaChatPersona");
            document.AppendChild(root);
            AppendText(document, root, "id", profile.id);
            AppendText(document, root, "label", profile.label ?? "");
            AppendText(document, root, "description", profile.description ?? "");
            AppendText(document, root, "storytellerLabel", profile.storytellerLabel ?? "");
            AppendText(document, root, "storytellerDescription", profile.storytellerDescription ?? "");
            AppendText(document, root, "storytellerPortraitFolder", profile.storytellerPortraitFolder ?? "");
            AppendText(document, root, "storytellerPortraitLargeName", profile.storytellerPortraitLargeName ?? "");
            AppendText(document, root, "storytellerPortraitTinyName", profile.storytellerPortraitTinyName ?? "");
            AppendText(document, root, "storytellerPortraitLargePath", profile.storytellerPortraitLargePath ?? "");
            AppendText(document, root, "storytellerPortraitTinyPath", profile.storytellerPortraitTinyPath ?? "");
            XmlElement prompt = document.CreateElement("prompt");
            prompt.AppendChild(document.CreateCDataSection(profile.prompt ?? ""));
            root.AppendChild(prompt);
            document.Save(profile.filePath);
        }

        public static void Delete(OrcaChatPersonaProfile profile)
        {
            if (profile == null || profile.readOnly)
            {
                return;
            }

            EnsureLoaded();
            localPersonas.RemoveAll(item => item.id == profile.id);
            if (!profile.filePath.NullOrEmpty() && File.Exists(profile.filePath))
            {
                File.Delete(profile.filePath);
            }

            if (DeepseekTheOrcaMod.Settings != null && DeepseekTheOrcaMod.Settings.chatPersonaDefName == profile.id)
            {
                DeepseekTheOrcaMod.Settings.chatPersonaDefName = BuiltInOrcaId;
                OrcaStorytellerAppearance.ApplyCurrent();
                OrcaChatWindowManager.Session.Clear();
            }
        }

        public static void ReloadLocal()
        {
            loadedLocal = false;
            localPersonas.Clear();
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (loadedLocal)
            {
                return;
            }

            loadedLocal = true;
            localPersonas.Clear();
            EnsureDirectory();
            foreach (string file in Directory.GetFiles(PersonaFolderPath, "*.xml"))
            {
                OrcaChatPersonaProfile profile = LoadFile(file);
                if (profile != null)
                {
                    localPersonas.Add(profile);
                }
            }
        }

        private static List<OrcaChatPersonaProfile> DefPersonas()
        {
            List<OrcaChatPersonaProfile> result = new List<OrcaChatPersonaProfile>();
            List<OrcaChatPersonaDef> defs = DefDatabase<OrcaChatPersonaDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                OrcaChatPersonaProfile profile = FromDef(defs[i]);
                if (profile != null)
                {
                    result.Add(profile);
                }
            }

            return result;
        }

        private static OrcaChatPersonaProfile DefPersona(string defName)
        {
            if (defName.NullOrEmpty())
            {
                return null;
            }

            OrcaChatPersonaDef def = DefDatabase<OrcaChatPersonaDef>.GetNamedSilentFail(defName);
            return FromDef(def);
        }

        private static OrcaChatPersonaProfile FromDef(OrcaChatPersonaDef def)
        {
            if (def == null || def.defName.NullOrEmpty())
            {
                return null;
            }

            OrcaChatPersonaProfile profile = new OrcaChatPersonaProfile
            {
                id = DefPrefix + def.defName,
                label = def.label.NullOrEmpty() ? def.defName : def.LabelCap.ToString(),
                description = def.description ?? "",
                storytellerLabel = def.storytellerLabel,
                storytellerDescription = def.storytellerDescription,
                storytellerPortraitFolder = def.storytellerPortraitFolder,
                storytellerPortraitLargeName = def.storytellerPortraitLargeName,
                storytellerPortraitTinyName = def.storytellerPortraitTinyName,
                storytellerPortraitLargePath = def.storytellerPortraitLargePath,
                storytellerPortraitTinyPath = def.storytellerPortraitTinyPath,
                prompt = def.prompt ?? "",
                readOnly = true,
                filePath = "",
                sourceMod = def.modContentPack == null ? "" : def.modContentPack.Name
            };
            NormalizeAppearance(profile);
            return profile;
        }

        private static OrcaChatPersonaProfile LoadFile(string file)
        {
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(file);
                XmlElement root = document.DocumentElement;
                if (root == null || root.Name != "OrcaChatPersona")
                {
                    return null;
                }

                string id = ReadText(root, "id");
                if (id.NullOrEmpty())
                {
                    id = LocalPrefix + Path.GetFileNameWithoutExtension(file);
                }
                if (!id.StartsWith(LocalPrefix, StringComparison.Ordinal))
                {
                    id = LocalPrefix + id;
                }

                OrcaChatPersonaProfile profile = new OrcaChatPersonaProfile
                {
                    id = id,
                    label = ReadText(root, "label").NullOrEmpty() ? Path.GetFileNameWithoutExtension(file) : ReadText(root, "label"),
                    description = ReadText(root, "description"),
                    storytellerLabel = ReadText(root, "storytellerLabel"),
                    storytellerDescription = ReadText(root, "storytellerDescription"),
                    storytellerPortraitFolder = ReadText(root, "storytellerPortraitFolder"),
                    storytellerPortraitLargeName = ReadText(root, "storytellerPortraitLargeName"),
                    storytellerPortraitTinyName = ReadText(root, "storytellerPortraitTinyName"),
                    storytellerPortraitLargePath = ReadText(root, "storytellerPortraitLargePath"),
                    storytellerPortraitTinyPath = ReadText(root, "storytellerPortraitTinyPath"),
                    prompt = ReadText(root, "prompt"),
                    readOnly = false,
                    filePath = file
                };
                NormalizeAppearance(profile);
                return profile;
            }
            catch (Exception ex)
            {
                Log.Warning("[Deepseek The Orca] Failed to load persona file " + file + ": " + ex.Message);
                return null;
            }
        }

        private static void EnsureDirectory()
        {
            Directory.CreateDirectory(PersonaFolderPath);
        }

        private static string PathFor(OrcaChatPersonaProfile profile)
        {
            string name = profile.id == null ? Guid.NewGuid().ToString("N") : profile.id.Replace(LocalPrefix, "");
            name = Regex.Replace(name, "[^A-Za-z0-9_.-]", "_");
            return Path.Combine(PersonaFolderPath, name + ".xml");
        }

        private static void AppendText(XmlDocument document, XmlElement root, string name, string value)
        {
            XmlElement element = document.CreateElement(name);
            element.InnerText = value ?? "";
            root.AppendChild(element);
        }

        private static string ReadText(XmlElement root, string name)
        {
            XmlNode node = root.SelectSingleNode(name);
            return node == null ? "" : node.InnerText;
        }

        public static void NormalizeAppearance(OrcaChatPersonaProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            if (profile.storytellerLabel.NullOrEmpty())
            {
                profile.storytellerLabel = profile.label.NullOrEmpty() ? DefaultStorytellerLabel : profile.label;
            }

            if (profile.storytellerDescription.NullOrEmpty())
            {
                profile.storytellerDescription = profile.description.NullOrEmpty() ? DefaultStorytellerDescription : profile.description;
            }

            if (profile.storytellerPortraitFolder.NullOrEmpty())
            {
                profile.storytellerPortraitFolder = DefaultStorytellerPortraitFolder;
            }

            if (profile.storytellerPortraitLargeName.NullOrEmpty())
            {
                profile.storytellerPortraitLargeName = DefaultStorytellerPortraitLargeName;
            }

            if (profile.storytellerPortraitTinyName.NullOrEmpty())
            {
                profile.storytellerPortraitTinyName = DefaultStorytellerPortraitTinyName;
            }

            if (profile.storytellerPortraitLargePath.NullOrEmpty())
            {
                profile.storytellerPortraitLargePath = TexturePath(profile.storytellerPortraitFolder, profile.storytellerPortraitLargeName);
            }
            if (profile.storytellerPortraitTinyPath.NullOrEmpty())
            {
                profile.storytellerPortraitTinyPath = TexturePath(profile.storytellerPortraitFolder, profile.storytellerPortraitTinyName);
            }
            profile.storytellerPortraitLargePath = CleanTexturePath(profile.storytellerPortraitLargePath);
            profile.storytellerPortraitTinyPath = CleanTexturePath(profile.storytellerPortraitTinyPath);
            if (profile.storytellerPortraitLargePath.NullOrEmpty())
            {
                profile.storytellerPortraitLargePath = DefaultStorytellerPortraitLargePath;
            }
            if (profile.storytellerPortraitTinyPath.NullOrEmpty())
            {
                profile.storytellerPortraitTinyPath = DefaultStorytellerPortraitTinyPath;
            }
        }

        private static string TexturePath(string folder, string fileName)
        {
            folder = CleanTexturePath(folder);
            fileName = CleanTexturePath(fileName);
            if (folder.NullOrEmpty())
            {
                return fileName;
            }

            if (fileName.NullOrEmpty())
            {
                return folder;
            }

            return folder + "/" + fileName;
        }

        private static string CleanTexturePath(string path)
        {
            return (path ?? "").Trim().Trim('/').Trim('\\').Replace('\\', '/');
        }
    }
}
