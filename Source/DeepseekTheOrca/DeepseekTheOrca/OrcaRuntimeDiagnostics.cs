using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Verse;

namespace DeepseekTheOrca
{
    internal static class OrcaRuntimeDiagnostics
    {
        private static readonly Queue<string> pending = new Queue<string>();
        private static readonly object sync = new object();

        // Do not use Exception.ToString: stack-trace deduplication patches may replace
        // it with a reference to a message that was never written to Player.log.
        internal static string ExceptionText(Exception error)
        {
            var text = new StringBuilder();
            AppendException(text, error, 0);
            return text.ToString();
        }

        private static void AppendException(StringBuilder text, Exception error, int depth)
        {
            if (error == null || depth > 8) return;
            text.AppendLine(error.GetType().FullName + ": " + error.Message);
            var frames = new StackTrace(error, true).GetFrames();
            if (frames != null)
                foreach (var frame in frames)
                {
                    var method = frame.GetMethod();
                    text.Append("  at ").Append(method == null ? "unknown" : (method.DeclaringType == null ? "" : method.DeclaringType.FullName + ".") + method.Name);
                    text.Append(" IL+").Append(frame.GetILOffset());
                    if (frame.GetFileLineNumber() > 0) text.Append(" line ").Append(frame.GetFileLineNumber());
                    text.AppendLine();
                }
            var aggregate = error as AggregateException;
            if (aggregate != null)
                foreach (var inner in aggregate.InnerExceptions) AppendException(text, inner, depth + 1);
            else AppendException(text, error.InnerException, depth + 1);
        }

        internal static void Record(string area, string detail)
        {
            lock (sync)
            {
                pending.Enqueue("[RimAgent] " + area + ": " + detail);
                while (pending.Count > 128) pending.Dequeue();
            }
        }

        internal static void Flush()
        {
            for (int i = 0; i < 8; i++)
            {
                string line;
                lock (sync) { if (pending.Count == 0) return; line = pending.Dequeue(); }
                Log.Message(line);
            }
        }
    }
}
