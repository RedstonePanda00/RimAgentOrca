using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Verse;

namespace RimAgent
{
    // Save-bound lossless storage, not model compression. Small independent blocks avoid
    // constructing another full-book JSON/XML tree while preserving every evidence field.
    public sealed class NovelRecordArchive : IExposable
    {
        public const int RecordsPerBlock = 128;
        public const int MaxExpandedBlockBytes = 16 * 1024 * 1024;
        private const int FormatVersion = 2;
        public List<string> blocks = new List<string>();
        public string Error { get; private set; }
        private readonly List<BlockCache> cache = new List<BlockCache>();
        public int LastEncodedBlockCount { get; private set; }

        public void Pack(IList<NovelRecord> records)
        {
            // A damaged archive must remain recoverable after the next game save. Never
            // replace its original blocks with the partial subset we could decode.
            if (!string.IsNullOrEmpty(Error)) return;
            LastEncodedBlockCount = 0;
            var next = new List<string>((records.Count + RecordsPerBlock - 1) / RecordsPerBlock);
            var nextCache = new List<BlockCache>(next.Capacity);
            for (int start = 0; start < records.Count; start += RecordsPerBlock)
            {
                int count = Math.Min(RecordsPerBlock, records.Count - start), blockIndex = start / RecordsPerBlock;
                if (blockIndex < cache.Count && cache[blockIndex].Matches(records, start, count))
                { next.Add(cache[blockIndex].encoded); nextCache.Add(cache[blockIndex]); continue; }
                using (var output = new MemoryStream())
                {
                    using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
                    using (var writer = new BinaryWriter(gzip, Encoding.UTF8, true))
                    {
                        writer.Write(FormatVersion);
                        writer.Write(count);
                        for (int i = 0; i < count; i++) Write(writer, records[start + i]);
                    }
                    string encoded = Convert.ToBase64String(output.ToArray());
                    next.Add(encoded); nextCache.Add(new BlockCache(encoded, records, start, count)); LastEncodedBlockCount++;
                }
            }
            blocks = next;
            cache.Clear(); cache.AddRange(nextCache);
        }

        public List<NovelRecord> Unpack()
        {
            var records = new List<NovelRecord>();
            var errors = new List<string>();
            var identities = new HashSet<string>();
            cache.Clear();
            for (int i = 0; i < blocks.Count; i++)
            {
                try
                {
                    var decoded = new List<NovelRecord>();
                    using (var encoded = new MemoryStream(Convert.FromBase64String(blocks[i])))
                    using (var gzip = new GZipStream(encoded, CompressionMode.Decompress))
                    using (var data = new MemoryStream())
                    {
                        CopyBounded(gzip, data); data.Position = 0;
                        using (var reader = new BinaryReader(data, Encoding.UTF8, true))
                        {
                            int version = reader.ReadInt32();
                            if (version != 1 && version != FormatVersion) throw new InvalidDataException("Unsupported evidence format: " + version);
                            int count = reader.ReadInt32();
                            if (count < 1 || count > RecordsPerBlock) throw new InvalidDataException("Invalid evidence block size: " + count);
                            for (int j = 0; j < count; j++) decoded.Add(Read(reader, version));
                            if (data.Position != data.Length) throw new InvalidDataException("Unexpected trailing evidence bytes");
                        }
                    }
                    var localIds = new HashSet<string>();
                    foreach (var record in decoded)
                        if (identities.Contains(record.id) || !localIds.Add(record.id)) throw new InvalidDataException("Duplicate evidence ID: " + record.id);
                    // Commit only after the whole block, including gzip checks, is valid.
                    records.AddRange(decoded);
                    identities.UnionWith(localIds);
                    cache.Add(new BlockCache(blocks[i], decoded, 0, decoded.Count));
                }
                catch (Exception ex)
                { errors.Add("Evidence block " + (i + 1) + ": " + ex.GetType().Name + ": " + ex.Message); }
            }
            Error = string.Join("\n", errors.ToArray());
            return records;
        }

        private static void CopyBounded(Stream source, MemoryStream destination)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                int count;
                while ((count = source.Read(buffer, 0, 8192)) > 0)
                {
                    if (destination.Length + count > MaxExpandedBlockBytes)
                        throw new InvalidDataException("Evidence block exceeds the 16 MiB decompression limit. Original compressed data is retained.");
                    destination.Write(buffer, 0, count);
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        private sealed class BlockCache
        {
            public readonly string encoded;
            private readonly NovelRecord[] snapshots;
            public BlockCache(string encoded, IList<NovelRecord> records, int start, int count)
            {
                this.encoded = encoded; snapshots = new NovelRecord[count];
                for (int i = 0; i < count; i++) snapshots[i] = records[start + i].Snapshot();
            }
            public bool Matches(IList<NovelRecord> records, int start, int count)
            {
                if (snapshots.Length != count) return false;
                for (int i = 0; i < count; i++)
                    if (!Same(snapshots[i], records[start + i])) return false;
                return true;
            }
            private static bool Same(NovelRecord a, NovelRecord b)
            {
                if (b == null || a.id != b.id || a.key != b.key || a.source != b.source || a.category != b.category
                    || a.subject != b.subject || a.location != b.location || a.summary != b.summary || a.detail != b.detail
                    || a.tick != b.tick || a.observedTick != b.observedTick || a.rangeStartTick != b.rangeStartTick
                    || a.rangeEndTick != b.rangeEndTick || a.historical != b.historical || a.stateBefore != b.stateBefore || a.stateAfter != b.stateAfter
                    || a.playerKnowledgeVersion != b.playerKnowledgeVersion) return false;
                if (a.personIds == null || b.personIds == null) return a.personIds == b.personIds;
                if (a.personIds.Count != b.personIds.Count) return false;
                for (int i = 0; i < a.personIds.Count; i++) if (a.personIds[i] != b.personIds[i]) return false;
                return true;
            }
        }

        private static void Text(BinaryWriter writer, string value)
        { writer.Write(value != null); if (value != null) writer.Write(value); }
        private static string Text(BinaryReader reader) { return reader.ReadBoolean() ? reader.ReadString() : null; }
        private static void Write(BinaryWriter w, NovelRecord r)
        {
            if (r == null) throw new InvalidDataException("Null evidence record");
            Text(w, r.id); Text(w, r.key); Text(w, r.source); Text(w, r.category);
            Text(w, r.subject); Text(w, r.location); Text(w, r.summary); Text(w, r.detail);
            w.Write(r.tick); w.Write(r.observedTick); w.Write(r.rangeStartTick); w.Write(r.rangeEndTick); w.Write(r.historical);
            Text(w, r.stateBefore); Text(w, r.stateAfter);
            w.Write(r.personIds == null ? -1 : r.personIds.Count);
            if (r.personIds != null) foreach (var id in r.personIds) Text(w, id);
            w.Write(r.playerKnowledgeVersion);
        }
        private static NovelRecord Read(BinaryReader r, int version)
        {
            var record = new NovelRecord { id = Text(r), key = Text(r), source = Text(r), category = Text(r),
                subject = Text(r), location = Text(r), summary = Text(r), detail = Text(r),
                tick = r.ReadInt32(), observedTick = r.ReadInt32(), rangeStartTick = r.ReadInt32(), rangeEndTick = r.ReadInt32(), historical = r.ReadBoolean(),
                stateBefore = Text(r), stateAfter = Text(r) };
            int count = r.ReadInt32();
            if (count < -1 || count > r.BaseStream.Length - r.BaseStream.Position)
                throw new InvalidDataException("Invalid evidence participant count");
            record.personIds = count < 0 ? null : new List<string>(count);
            for (int i = 0; i < count; i++) record.personIds.Add(Text(r));
            record.playerKnowledgeVersion = version >= 2 ? r.ReadInt32() : 0;
            if (string.IsNullOrEmpty(record.id) || string.IsNullOrEmpty(record.key))
                throw new InvalidDataException("Evidence identity is missing");
            return record;
        }
        public void ExposeData()
        {
            Scribe_Collections.Look(ref blocks, "blocks", LookMode.Value);
            blocks = blocks ?? new List<string>();
        }
    }
}
