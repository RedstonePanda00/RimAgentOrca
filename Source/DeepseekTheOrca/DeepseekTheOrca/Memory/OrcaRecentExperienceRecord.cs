using System;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaRecentExperienceRecord
    {
        public string id = "";
        public string personaId = "";
        public string saveId = "";
        public string source = "";
        public string text = "";
        public int tick;
        public long createdAt;

        public static OrcaRecentExperienceRecord Create(string source, string text)
        {
            return new OrcaRecentExperienceRecord
            {
                id = Guid.NewGuid().ToString("N"),
                personaId = OrcaLongTermMemoryService.CurrentPersonaId(),
                saveId = OrcaLongTermMemoryService.CurrentSaveId(),
                source = source ?? "",
                text = (text ?? "").Trim(),
                tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame,
                createdAt = OrcaMemoryRecord.NowUnixSeconds()
            };
        }
    }
}
