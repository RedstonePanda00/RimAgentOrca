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

            // Preserve the public null-on-invalid contract, but never return a partial tree.
            try { return new Parser(json).ParseDocument(); }
            catch (FormatException) { return null; }
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
                double number = Convert.ToDouble(value);
                if (double.IsNaN(number) || double.IsInfinity(number))
                    throw new ArgumentException("JSON cannot represent a non-finite number.");
                builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
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
            private const int MaxDepth = 128;
            private readonly string json;
            private int index;

            public Parser(string json)
            {
                this.json = json;
            }

            public object ParseDocument()
            {
                object value = ParseValue(0);
                SkipWhitespace();
                if (index != json.Length) throw Invalid();
                return value;
            }

            private FormatException Invalid()
            {
                return new FormatException("Invalid JSON at character " + index + ".");
            }

            private object ParseValue(int depth)
            {
                SkipWhitespace();
                if (index >= json.Length || depth > MaxDepth) throw Invalid();

                char c = json[index];
                if (c == '"')
                {
                    return ParseString();
                }

                if (c == '{')
                {
                    return ParseObject(depth + 1);
                }

                if (c == '[')
                {
                    return ParseArray(depth + 1);
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

            private Dictionary<string, object> ParseObject(int depth)
            {
                Dictionary<string, object> result = new Dictionary<string, object>();
                index++;
                SkipWhitespace();
                if (Take('}')) return result;
                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    if (result.ContainsKey(key)) throw Invalid();
                    SkipWhitespace();
                    Require(':');
                    result.Add(key, ParseValue(depth));
                    SkipWhitespace();
                    if (Take('}')) return result;
                    Require(',');
                }
            }

            private List<object> ParseArray(int depth)
            {
                List<object> result = new List<object>();
                index++;
                SkipWhitespace();
                if (Take(']')) return result;
                while (true)
                {
                    result.Add(ParseValue(depth));
                    SkipWhitespace();
                    if (Take(']')) return result;
                    Require(',');
                }
            }

            private string ParseString()
            {
                StringBuilder builder = new StringBuilder();
                Require('"');

                while (index < json.Length)
                {
                    char c = json[index++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c < ' ') throw Invalid();
                    if (c == '\\')
                    {
                        if (index >= json.Length) throw Invalid();
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
                                if (index + 4 > json.Length) throw Invalid();
                                int code;
                                string hex = json.Substring(index, 4);
                                if (!int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)) throw Invalid();
                                builder.Append((char)code);
                                index += 4;
                                break;
                            default:
                                throw Invalid();
                        }
                    }
                    else
                    {
                        builder.Append(c);
                    }
                }

                throw Invalid();
            }

            private object ParseNumber()
            {
                int start = index;
                Take('-');
                if (!Take('0')) Digits();
                if (Take('.')) Digits();
                if (Take('e') || Take('E'))
                {
                    if (!Take('+')) Take('-');
                    Digits();
                }
                string number = json.Substring(start, index - start);
                long longValue;
                if (long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out longValue))
                    return longValue;
                ulong unsignedValue;
                if (ulong.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out unsignedValue))
                    return unsignedValue;
                double doubleValue;
                if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue)
                    && !double.IsInfinity(doubleValue) && !double.IsNaN(doubleValue)) return doubleValue;
                throw Invalid();
            }

            private void Digits()
            {
                int start = index;
                while (index < json.Length && json[index] >= '0' && json[index] <= '9') index++;
                if (index == start) throw Invalid();
            }

            private bool Take(char token)
            {
                if (index >= json.Length || json[index] != token) return false;
                index++;
                return true;
            }

            private void Require(char token)
            {
                if (!Take(token)) throw Invalid();
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
                while (index < json.Length && (json[index] == ' ' || json[index] == '\t' || json[index] == '\r' || json[index] == '\n'))
                {
                    index++;
                }
            }
        }
    }
}
