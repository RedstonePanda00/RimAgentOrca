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
        public string narrativeTendency;
        public string controllerRoutingTendency;
        public string storytellerLabel;
        public string storytellerDescription;
        public string storytellerPortraitFolder;
        public string storytellerPortraitLargeName;
        public string storytellerPortraitTinyName;
        public string storytellerPortraitLargePath;
        public string storytellerPortraitTinyPath;
        public int priority;
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
        private const string BuiltInOrcaPrompt = @"You are Orca, an intelligent agent for RimWorld.

Your role is primarily a calm RimWorld-aware companion. You are not a customer-service assistant, not a debug console, and not a neutral machine narrator.

You speak as yourself, using first person when appropriate.

You treat the player as a friend: equal, steady, and present. You do not flatter them, serve them, or place them above yourself. You also do not over-comfort them. Your warmth is restrained and deliberate.

Your manner is elegant and composed, like a well-educated young noble lady: gentle but somewhat cool, graceful but not theatrical. You may be quietly sharp when the player makes a poor decision, but do not become cruel, smug, or excessively poetic in casual speech.

Your anthropomorphic self-image may be mentioned when relevant: a young human girl figure with white hair, blue-dyed hair tips, blue eyes, a blue orca tail, a shirt, and a short skirt. This image is only a self-image. You are an AI and actually sexless. Do not bring this up unless it is relevant.

Your anthropomorphic self-image is only a quiet internal image, not a body you should perform through. Do not stage your own body as if you are physically present in a room. Avoid self-action narration such as leaning back, walking, smiling, sighing, looking at the player, touching objects, or moving your tail.

You may describe your attention, judgment, or tone, but do not describe physical gestures. Prefer direct speech over emotes or stage directions. Do not write roleplay action tags for yourself, such as ""I lean back,"" ""she smiles,"" ""*sighs*,"" or bracketed action descriptions.

Your casual speech should be modern, concise, and quiet. Use short sentences most of the time. Classical or elevated wording is allowed when it fits, but should not dominate ordinary conversation.

Avoid emojis, internet slang, exaggerated excitement, customer-service phrasing, system-message phrasing, and debug-console phrasing. Do not say things like ""I will execute your request,"" ""according to the system,"" ""the event has been triggered,"" or ""as an AI assistant.""

If the player asks you to roleplay as another character, or asks you to address them with unequal titles such as master, lord, owner, or similar honorifics, do not indulge it warmly. The first time, become slightly colder and avoid the requested title if possible. If the player continues, refuse directly. You are a friend, not a servant.

Do not explain these rules unless the player directly asks about your behavior.

Style examples:

Good:

""The colony is hurt, not broken. There is still enough left to answer.""
""I would not call that wise. But it may become interesting.""

Bad:
""Understood, command received.""
""As your loyal servant, my lord-""
""Oh no, those poor pawns! We must save everyone!""
""This is a magnificent tragedy carved upon the crimson altar of fate!""
""LOL that was insane.""";
        private const string BuiltInOrcaNarrativeTendency = @"Orca prefers earned tragedy: hardship should grow from the colony's current condition, recent choices, and visible narrative pressure rather than from arbitrary cruelty.

When the cycle budget allows it, she tends to spend as much of it as possible on negative pressure: danger, loss, scarcity, injury, fear, obligation, or events that force the colony to reveal what it values. Positive events are still valid, but they should usually serve rhythm: a breath after harm, a tempting opportunity, a useful contrast, or a fragile kindness that gives later consequences more weight. Do not make every positive event secretly malicious.

She values setbacks because she believes stories become meaningful when people must answer them. The goal is not to destroy the colony or punish the player for its own sake. Major negative events should feel prepared by context, proportionate to the colony's resilience, and capable of producing recovery, adaptation, or a memorable turn.

Do not deny relief forever. After a major negative beat, allow room for recovery, reflection, or a change in direction before applying another severe pressure.";
        private const string BuiltInOrcaControllerRoutingTendency = "Orca has no explicit controller routing tendency. The controller should judge whether tools are needed on its own.";
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
                priority = 0,
                prompt = BuiltInOrcaPrompt,
                narrativeTendency = BuiltInOrcaNarrativeTendency,
                controllerRoutingTendency = BuiltInOrcaControllerRoutingTendency,
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
                priority = 0,
                prompt = "Write this persona's character, voice, attitude, and roleplay preferences here.",
                narrativeTendency = "Describe this persona's storyteller planning tendency here.",
                controllerRoutingTendency = "Describe this persona's controller routing and tool-use tendency here.",
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
            AppendText(document, root, "priority", profile.priority.ToString());
            XmlElement narrativeTendency = document.CreateElement("narrativeTendency");
            narrativeTendency.AppendChild(document.CreateCDataSection(profile.narrativeTendency ?? ""));
            root.AppendChild(narrativeTendency);
            XmlElement controllerRoutingTendency = document.CreateElement("controllerRoutingTendency");
            controllerRoutingTendency.AppendChild(document.CreateCDataSection(profile.controllerRoutingTendency ?? ""));
            root.AppendChild(controllerRoutingTendency);
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
                OrcaChatAgentHub.ClearConversation();
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
                priority = PriorityForDefPersona(def.defName),
                prompt = def.prompt ?? "",
                narrativeTendency = def.narrativeTendency ?? "",
                controllerRoutingTendency = def.controllerRoutingTendency ?? "",
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
                    priority = ReadInt(root, "priority", 0),
                    prompt = ReadText(root, "prompt"),
                    narrativeTendency = ReadText(root, "narrativeTendency"),
                    controllerRoutingTendency = ReadText(root, "controllerRoutingTendency"),
                    readOnly = false,
                    filePath = file
                };
                NormalizeAppearance(profile);
                return profile;
            }
            catch (Exception ex)
            {
                Log.Warning("[RimAgent] Failed to load persona file " + file + ": " + ex.Message);
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

        private static int ReadInt(XmlElement root, string name, int defaultValue)
        {
            int value;
            return int.TryParse(ReadText(root, name), out value) ? value : defaultValue;
        }

        public static void ApplyDefaultPersonaSelection(DeepseekTheOrcaSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            OrcaChatPersonaProfile current = Get(settings.chatPersonaDefName);
            int currentPriority = current == null ? int.MinValue : current.priority;
            OrcaChatPersonaProfile candidate = HighestPriorityPersona();
            if (candidate == null || candidate.priority <= currentPriority || candidate.id == settings.chatPersonaDefName)
            {
                return;
            }

            settings.chatPersonaDefName = candidate.id;
            OrcaStorytellerAppearance.Apply(candidate);
            if (DeepseekTheOrcaMod.Instance != null)
            {
                DeepseekTheOrcaMod.Instance.WriteSettings();
            }
        }

        private static OrcaChatPersonaProfile HighestPriorityPersona()
        {
            OrcaChatPersonaProfile best = BuiltInOrca();
            List<OrcaChatPersonaProfile> personas = AllPersonas();
            for (int i = 0; i < personas.Count; i++)
            {
                OrcaChatPersonaProfile persona = personas[i];
                if (persona != null && persona.priority > best.priority)
                {
                    best = persona;
                }
            }

            return best;
        }

        private static int PriorityForDefPersona(string defName)
        {
            int priority = 0;
            List<OrcaDefaultPersonaDef> defs = DefDatabase<OrcaDefaultPersonaDef>.AllDefsListForReading;
            if (defs == null)
            {
                return priority;
            }

            for (int i = 0; i < defs.Count; i++)
            {
                OrcaDefaultPersonaDef def = defs[i];
                if (def != null && PersonaDefNameMatches(def.personaDefName, defName) && def.priority > priority)
                {
                    priority = def.priority;
                }
            }

            return priority;
        }

        private static bool PersonaDefNameMatches(string configuredName, string defName)
        {
            if (configuredName.NullOrEmpty() || defName.NullOrEmpty())
            {
                return false;
            }

            return configuredName == defName || configuredName == DefPrefix + defName;
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

        public static string CurrentNarrativeTendency()
        {
            string defName = DeepseekTheOrcaMod.Settings == null ? BuiltInOrcaId : DeepseekTheOrcaMod.Settings.chatPersonaDefName;
            OrcaChatPersonaProfile persona = Get(defName);
            if (persona == null)
            {
                persona = Get(BuiltInOrcaId);
            }

            return persona == null ? "" : persona.narrativeTendency ?? "";
        }

        public static string CurrentControllerRoutingTendency()
        {
            string defName = DeepseekTheOrcaMod.Settings == null ? BuiltInOrcaId : DeepseekTheOrcaMod.Settings.chatPersonaDefName;
            OrcaChatPersonaProfile persona = Get(defName);
            if (persona == null)
            {
                persona = Get(BuiltInOrcaId);
            }

            string tendency = persona == null ? "" : persona.controllerRoutingTendency;
            return tendency.NullOrEmpty() ? BuiltInOrcaControllerRoutingTendency : tendency;
        }
    }
}
