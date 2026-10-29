using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Verse;

namespace RimAgent
{
    internal static class NovelSavedText
    {
        // XML attribute normalization in Scribe can change literal newlines. Payloads
        // used to resume requests must round-trip byte for byte, including embedded JSON.
        public static void Look(ref string value, string name)
        {
            string encoded = "";
            if (Scribe.mode == LoadSaveMode.Saving && !string.IsNullOrEmpty(value))
            {
                using (var output = new MemoryStream())
                {
                    using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
                    { var bytes = Encoding.UTF8.GetBytes(value); gzip.Write(bytes, 0, bytes.Length); }
                    encoded = Convert.ToBase64String(output.ToArray());
                }
            }
            Scribe_Values.Look(ref encoded, name + "Gzip", "");
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;
            if (string.IsNullOrEmpty(encoded)) { value = ""; return; }
            using (var input = new MemoryStream(Convert.FromBase64String(encoded)))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8)) value = reader.ReadToEnd();
        }
    }
}
