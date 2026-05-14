using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DeepseekTheOrca
{
    public static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (json == null)
            {
                return null;
            }

            return new Parser(json).ParseValue();
        }

        public static string Serialize(object value)
        {
            StringBuilder builder = new StringBuilder();
            WriteValue(builder, value);
            return builder.ToString();
        }

        private static void WriteValue(StringBuilder builder, object value)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            string stringValue = value as string;
            if (stringValue != null)
            {
                WriteString(builder, stringValue);
                return;
            }

            if (value is bool)
            {
                builder.Append((bool)value ? "true" : "false");
                return;
            }

            IDictionary dictionary = value as IDictionary;
            if (dictionary != null)
            {
                WriteObject(builder, dictionary);
                return;
            }

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                WriteArray(builder, enumerable);
                return;
            }

            if (value is float || value is double || value is decimal)
            {
                builder.Append(Convert.ToDouble(value).ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            if (value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong)
            {
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }

            WriteString(builder, value.ToString());
        }

        private static void WriteObject(StringBuilder builder, IDictionary dictionary)
        {
            bool first = true;
            builder.Append('{');
            foreach (DictionaryEntry entry in dictionary)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                WriteString(builder, entry.Key.ToString());
                builder.Append(':');
                WriteValue(builder, entry.Value);
                first = false;
            }
            builder.Append('}');
        }

        private static void WriteArray(StringBuilder builder, IEnumerable enumerable)
        {
            bool first = true;
            builder.Append('[');
            foreach (object value in enumerable)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                WriteValue(builder, value);
                first = false;
            }
            builder.Append(']');
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
        }

        private sealed class Parser
        {
            private readonly string json;
            private int index;

            public Parser(string json)
            {
                this.json = json;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (index >= json.Length)
                {
                    return null;
                }

                char c = json[index];
                if (c == '"')
                {
                    return ParseString();
                }

                if (c == '{')
                {
                    return ParseObject();
                }

                if (c == '[')
                {
                    return ParseArray();
                }

                if (Match("true"))
                {
                    return true;
                }

                if (Match("false"))
                {
                    return false;
                }

                if (Match("null"))
                {
                    return null;
                }

                return ParseNumber();
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> result = new Dictionary<string, object>();
                index++;
                while (true)
                {
                    SkipWhitespace();
                    if (index >= json.Length)
                    {
                        return result;
                    }

                    if (json[index] == '}')
                    {
                        index++;
                        return result;
                    }

                    string key = ParseString();
                    SkipWhitespace();
                    if (index < json.Length && json[index] == ':')
                    {
                        index++;
                    }

                    result[key] = ParseValue();
                    SkipWhitespace();
                    if (index < json.Length && json[index] == ',')
                    {
                        index++;
                    }
                }
            }

            private List<object> ParseArray()
            {
                List<object> result = new List<object>();
                index++;
                while (true)
                {
                    SkipWhitespace();
                    if (index >= json.Length)
                    {
                        return result;
                    }

                    if (json[index] == ']')
                    {
                        index++;
                        return result;
                    }

                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (index < json.Length && json[index] == ',')
                    {
                        index++;
                    }
                }
            }

            private string ParseString()
            {
                StringBuilder builder = new StringBuilder();
                if (index < json.Length && json[index] == '"')
                {
                    index++;
                }

                while (index < json.Length)
                {
                    char c = json[index++];
                    if (c == '"')
                    {
                        break;
                    }

                    if (c == '\\' && index < json.Length)
                    {
                        char escaped = json[index++];
                        switch (escaped)
                        {
                            case '"':
                            case '\\':
                            case '/':
                                builder.Append(escaped);
                                break;
                            case 'b':
                                builder.Append('\b');
                                break;
                            case 'f':
                                builder.Append('\f');
                                break;
                            case 'n':
                                builder.Append('\n');
                                break;
                            case 'r':
                                builder.Append('\r');
                                break;
                            case 't':
                                builder.Append('\t');
                                break;
                            case 'u':
                                if (index + 4 <= json.Length)
                                {
                                    string hex = json.Substring(index, 4);
                                    builder.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                                    index += 4;
                                }
                                break;
                        }
                    }
                    else
                    {
                        builder.Append(c);
                    }
                }

                return builder.ToString();
            }

            private object ParseNumber()
            {
                int start = index;
                while (index < json.Length && "-+0123456789.eE".IndexOf(json[index]) >= 0)
                {
                    index++;
                }

                string number = json.Substring(start, index - start);
                if (number.IndexOf('.') >= 0 || number.IndexOf('e') >= 0 || number.IndexOf('E') >= 0)
                {
                    double doubleValue;
                    if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
                    {
                        return doubleValue;
                    }
                }

                long longValue;
                if (long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out longValue))
                {
                    return longValue;
                }

                return 0;
            }

            private bool Match(string token)
            {
                if (index + token.Length > json.Length)
                {
                    return false;
                }

                if (string.Compare(json, index, token, 0, token.Length, StringComparison.Ordinal) != 0)
                {
                    return false;
                }

                index += token.Length;
                return true;
            }

            private void SkipWhitespace()
            {
                while (index < json.Length && char.IsWhiteSpace(json[index]))
                {
                    index++;
                }
            }
        }
    }
}
