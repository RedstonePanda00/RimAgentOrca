using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Verse;
namespace DeepseekTheOrca
{
    public static partial class OrcaSkillManager
    {
        private static SkillMarkdown ParseSkillMarkdown(string text, string folderName, string filePath)
        {
            SkillMarkdown result = new SkillMarkdown();
            text = text ?? "";
            string metadata = "";
            string instructions = text;
            if (text.StartsWith("---"))
            {
                int start = text.IndexOf('\n');
                int end = start < 0 ? -1 : text.IndexOf("\n---", start + 1, StringComparison.Ordinal);
                if (end >= 0)
                {
                    metadata = text.Substring(start + 1, end - start - 1);
                    int instructionStart = end + 4;
                    instructions = instructionStart >= text.Length ? "" : text.Substring(instructionStart).TrimStart('\r', '\n');
                }
            }

            ParseMetadata(metadata, result, folderName, filePath);
            result.instructions = instructions.Trim();
            return result;
        }

        private static void ParseMetadata(string metadata, SkillMarkdown result, string folderName, string filePath)
        {
            if (metadata.NullOrEmpty() || result == null)
            {
                return;
            }

            string currentList = "";
            string[] lines = metadata.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                string rawLine = lines[i];
                string line = rawLine.Trim();
                if (line.NullOrEmpty() || line.StartsWith("#"))
                {
                    continue;
                }

                if (line.StartsWith("- "))
                {
                    if (currentList.NullOrEmpty())
                    {
                        WarnSkillMetadata(filePath, i + 1, "list item without a list key");
                        continue;
                    }
                    string value;
                    if (!TryParseYamlScalar(line.Substring(2).Trim(), out value))
                    {
                        WarnSkillMetadata(filePath, i + 1, "invalid list item");
                        continue;
                    }
                    AddMetadataListValue(result, currentList, value);
                    continue;
                }

                int separator = FindYamlKeySeparator(line);
                if (separator < 0)
                {
                    WarnSkillMetadata(filePath, i + 1, "expected 'key: value' or '- item'");
                    continue;
                }

                string key = line.Substring(0, separator).Trim();
                string valueText = line.Substring(separator + 1).Trim();
                currentList = "";
                switch (key)
                {
                    case "name":
                        if (TryParseYamlScalar(valueText, out result.name))
                        {
                            result.name = result.name.Trim();
                        }
                        else
                        {
                            WarnSkillMetadata(filePath, i + 1, "invalid name scalar");
                        }
                        break;
                    case "displayName":
                        if (TryParseYamlScalar(valueText, out result.displayName))
                        {
                            result.displayName = result.displayName.Trim();
                        }
                        else
                        {
                            WarnSkillMetadata(filePath, i + 1, "invalid displayName scalar");
                        }
                        break;
                    case "description":
                        if (TryParseYamlScalar(valueText, out result.description))
                        {
                            result.description = result.description.Trim();
                        }
                        else
                        {
                            WarnSkillMetadata(filePath, i + 1, "invalid description scalar");
                        }
                        break;
                    case "enabled":
                    case "defaultEnabled":
                        bool enabled;
                        string enabledValue;
                        if (TryParseYamlScalar(valueText, out enabledValue) && bool.TryParse(enabledValue, out enabled))
                        {
                            result.enabled = enabled;
                        }
                        else
                        {
                            WarnSkillMetadata(filePath, i + 1, "enabled must be true or false");
                        }
                        break;
                    case "activation":
                        string activation;
                        if (TryParseYamlScalar(valueText, out activation))
                        {
                            result.activation = NormalizeActivation(activation);
                        }
                        else
                        {
                            WarnSkillMetadata(filePath, i + 1, "invalid activation scalar");
                        }
                        break;
                    case "triggerHints":
                    case "contexts":
                    case "allowedTools":
                        currentList = key;
                        if (!valueText.NullOrEmpty())
                        {
                            List<string> values;
                            if (TryParseYamlInlineList(valueText, out values))
                            {
                                for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
                                {
                                    AddMetadataListValue(result, currentList, values[valueIndex]);
                                }
                                currentList = "";
                            }
                            else
                            {
                                string scalar;
                                if (TryParseYamlScalar(valueText, out scalar))
                                {
                                    AddMetadataListValue(result, currentList, scalar);
                                    currentList = "";
                                }
                                else
                                {
                                    WarnSkillMetadata(filePath, i + 1, "invalid list value");
                                }
                            }
                        }
                        break;
                    default:
                        WarnSkillMetadata(filePath, i + 1, "unknown metadata key '" + key + "'");
                        break;
                }
            }
        }

        private static int FindYamlKeySeparator(string line)
        {
            bool inSingle = false;
            bool inDouble = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    continue;
                }
                if (ch == '"' && !inSingle && (i == 0 || line[i - 1] != '\\'))
                {
                    inDouble = !inDouble;
                    continue;
                }
                if (ch == ':' && !inSingle && !inDouble)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryParseYamlScalar(string text, out string value)
        {
            value = "";
            text = text == null ? "" : StripYamlComment(text.Trim());
            if (text.NullOrEmpty())
            {
                return true;
            }

            if (text.StartsWith("\"", StringComparison.Ordinal))
            {
                if (!text.EndsWith("\"", StringComparison.Ordinal) || text.Length < 2)
                {
                    return false;
                }
                value = UnescapeDoubleQuotedScalar(text.Substring(1, text.Length - 2));
                return true;
            }

            if (text.StartsWith("'", StringComparison.Ordinal))
            {
                if (!text.EndsWith("'", StringComparison.Ordinal) || text.Length < 2)
                {
                    return false;
                }
                value = text.Substring(1, text.Length - 2).Replace("''", "'");
                return true;
            }

            if (text.StartsWith("[", StringComparison.Ordinal) || text.StartsWith("{", StringComparison.Ordinal))
            {
                return false;
            }

            value = text.Trim();
            return true;
        }

        private static bool TryParseYamlInlineList(string text, out List<string> values)
        {
            values = new List<string>();
            text = text == null ? "" : StripYamlComment(text.Trim());
            if (!text.StartsWith("[", StringComparison.Ordinal) || !text.EndsWith("]", StringComparison.Ordinal))
            {
                return false;
            }

            string body = text.Substring(1, text.Length - 2);
            StringBuilder item = new StringBuilder();
            bool inSingle = false;
            bool inDouble = false;
            for (int i = 0; i < body.Length; i++)
            {
                char ch = body[i];
                if (ch == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    item.Append(ch);
                    continue;
                }
                if (ch == '"' && !inSingle && (i == 0 || body[i - 1] != '\\'))
                {
                    inDouble = !inDouble;
                    item.Append(ch);
                    continue;
                }
                if (ch == ',' && !inSingle && !inDouble)
                {
                    string parsed;
                    if (!TryParseYamlScalar(item.ToString(), out parsed))
                    {
                        return false;
                    }
                    if (!parsed.NullOrEmpty())
                    {
                        values.Add(parsed);
                    }
                    item.Length = 0;
                    continue;
                }
                item.Append(ch);
            }

            if (inSingle || inDouble)
            {
                return false;
            }

            string finalValue;
            if (!TryParseYamlScalar(item.ToString(), out finalValue))
            {
                return false;
            }
            if (!finalValue.NullOrEmpty())
            {
                values.Add(finalValue);
            }

            return true;
        }

        private static string StripYamlComment(string text)
        {
            if (text.NullOrEmpty())
            {
                return "";
            }

            bool inSingle = false;
            bool inDouble = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    continue;
                }
                if (ch == '"' && !inSingle && (i == 0 || text[i - 1] != '\\'))
                {
                    inDouble = !inDouble;
                    continue;
                }
                if (ch == '#' && !inSingle && !inDouble && (i == 0 || char.IsWhiteSpace(text[i - 1])))
                {
                    return text.Substring(0, i).TrimEnd();
                }
            }

            return text;
        }

        private static string UnescapeDoubleQuotedScalar(string text)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch != '\\' || i >= text.Length - 1)
                {
                    builder.Append(ch);
                    continue;
                }

                char next = text[++i];
                switch (next)
                {
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    default:
                        builder.Append(next);
                        break;
                }
            }

            return builder.ToString();
        }

        private static string QuoteYamlScalar(string value)
        {
            value = value ?? "";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }

        private static void WarnSkillMetadata(string filePath, int line, string message)
        {
            Log.Warning("[Deepseek The Orca] Skill metadata warning in " + filePath + ":" + line + " - " + message + ".");
        }

        private static void AddMetadataListValue(SkillMarkdown result, string listName, string value)
        {
            value = value == null ? "" : value.Trim().Trim('"');
            if (value.NullOrEmpty())
            {
                return;
            }

            if (listName == "triggerHints")
            {
                result.triggerHints.Add(value);
            }
            else if (listName == "contexts")
            {
                value = NormalizeContextTag(value);
                if (!value.NullOrEmpty())
                {
                    result.contexts.Add(value);
                }
            }
            else if (listName == "allowedTools")
            {
                result.allowedTools.Add(value);
            }
        }
    }
}
