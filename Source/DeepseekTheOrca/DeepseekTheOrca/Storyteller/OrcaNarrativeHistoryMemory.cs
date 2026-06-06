using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaNarrativeHistorySnapshotRecord : IExposable
    {
        public int colonists;
        public int downedColonists;
        public int mentalStateColonists;
        public int injuredColonists;
        public float averageMood;
        public float humanEdibleNutrition;
        public int medicineCount;
        public float combatCapacity;
        public string medicalChain = "healthy";
        public string combatChain = "healthy";
        public string foodChain = "healthy";
        public string moraleChain = "healthy";

        public static OrcaNarrativeHistorySnapshotRecord From(ColonyDeepSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return new OrcaNarrativeHistorySnapshotRecord();
            }

            return new OrcaNarrativeHistorySnapshotRecord
            {
                colonists = snapshot.colonists,
                downedColonists = snapshot.downedColonists,
                mentalStateColonists = snapshot.mentalStateColonists,
                injuredColonists = snapshot.injuredColonists,
                averageMood = snapshot.averageMood,
                humanEdibleNutrition = snapshot.humanEdibleNutrition,
                medicineCount = snapshot.medicineCount,
                combatCapacity = snapshot.combatCapacity,
                medicalChain = snapshot.medicalChain == null ? "healthy" : snapshot.medicalChain.state,
                combatChain = snapshot.combatChain == null ? "healthy" : snapshot.combatChain.state,
                foodChain = snapshot.foodChain == null ? "healthy" : snapshot.foodChain.state,
                moraleChain = snapshot.moraleChain == null ? "healthy" : snapshot.moraleChain.state
            };
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref colonists, "colonists");
            Scribe_Values.Look(ref downedColonists, "downedColonists");
            Scribe_Values.Look(ref mentalStateColonists, "mentalStateColonists");
            Scribe_Values.Look(ref injuredColonists, "injuredColonists");
            Scribe_Values.Look(ref averageMood, "averageMood");
            Scribe_Values.Look(ref humanEdibleNutrition, "humanEdibleNutrition");
            Scribe_Values.Look(ref medicineCount, "medicineCount");
            Scribe_Values.Look(ref combatCapacity, "combatCapacity");
            Scribe_Values.Look(ref medicalChain, "medicalChain", "healthy");
            Scribe_Values.Look(ref combatChain, "combatChain", "healthy");
            Scribe_Values.Look(ref foodChain, "foodChain", "healthy");
            Scribe_Values.Look(ref moraleChain, "moraleChain", "healthy");
        }
    }

    public sealed class OrcaNarrativeHistoryRecord : IExposable
    {
        public string eventType = "";
        public string incidentDef = "";
        public float estimatedThreatPoints;
        public int startTick;
        public bool captured9000;
        public bool captured18000;
        public string outcomeLabel = "";
        public int deathDelta;
        public int downedDelta;
        public int mentalBreakDelta;
        public float foodDelta;
        public int medicineDelta;
        public float moodDelta;
        public string chainDamage = "";
        public OrcaNarrativeHistorySnapshotRecord preSnapshot = new OrcaNarrativeHistorySnapshotRecord();
        public OrcaNarrativeHistorySnapshotRecord postSnapshot9000 = new OrcaNarrativeHistorySnapshotRecord();
        public OrcaNarrativeHistorySnapshotRecord postSnapshot18000 = new OrcaNarrativeHistorySnapshotRecord();

        public void ExposeData()
        {
            Scribe_Values.Look(ref eventType, "eventType", "");
            Scribe_Values.Look(ref incidentDef, "incidentDef", "");
            Scribe_Values.Look(ref estimatedThreatPoints, "estimatedThreatPoints");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref captured9000, "captured9000");
            Scribe_Values.Look(ref captured18000, "captured18000");
            Scribe_Values.Look(ref outcomeLabel, "outcomeLabel", "");
            Scribe_Values.Look(ref deathDelta, "deathDelta");
            Scribe_Values.Look(ref downedDelta, "downedDelta");
            Scribe_Values.Look(ref mentalBreakDelta, "mentalBreakDelta");
            Scribe_Values.Look(ref foodDelta, "foodDelta");
            Scribe_Values.Look(ref medicineDelta, "medicineDelta");
            Scribe_Values.Look(ref moodDelta, "moodDelta");
            Scribe_Values.Look(ref chainDamage, "chainDamage", "");
            Scribe_Deep.Look(ref preSnapshot, "preSnapshot");
            Scribe_Deep.Look(ref postSnapshot9000, "postSnapshot9000");
            Scribe_Deep.Look(ref postSnapshot18000, "postSnapshot18000");
        }
    }

    public static class OrcaNarrativeHistoryMemory
    {
        private const int FirstCaptureTicks = 9000;
        private const int SecondCaptureTicks = 18000;
        private const int MaxRecords = 40;
        private static List<OrcaNarrativeHistoryRecord> records = new List<OrcaNarrativeHistoryRecord>();

        public static void ExposeData()
        {
            Scribe_Collections.Look(ref records, "orcaNarrativeHistoryRecords", LookMode.Deep);
            if (records == null)
            {
                records = new List<OrcaNarrativeHistoryRecord>();
            }
        }

        public static void Tick()
        {
            if (Find.TickManager == null || Find.CurrentMap == null)
            {
                return;
            }

            int ticksGame = Find.TickManager.TicksGame;
            for (int i = 0; i < records.Count; i++)
            {
                OrcaNarrativeHistoryRecord record = records[i];
                if (record == null)
                {
                    continue;
                }

                if (!record.captured9000 && ticksGame - record.startTick >= FirstCaptureTicks)
                {
                    CapturePost(record, ColonyDeepSnapshot.Capture(Find.CurrentMap), first: true);
                }

                if (!record.captured18000 && ticksGame - record.startTick >= SecondCaptureTicks)
                {
                    CapturePost(record, ColonyDeepSnapshot.Capture(Find.CurrentMap), first: false);
                    FinalizeOutcome(record);
                }
            }

            while (records.Count > MaxRecords)
            {
                records.RemoveAt(0);
            }
        }

        public static void BeginIncident(string incidentDef, float estimatedThreatPoints, Map map)
        {
            if (map == null)
            {
                return;
            }

            OrcaNarrativeHistoryRecord record = new OrcaNarrativeHistoryRecord();
            record.eventType = "incident";
            record.incidentDef = incidentDef ?? "";
            record.estimatedThreatPoints = estimatedThreatPoints;
            record.startTick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            record.preSnapshot = OrcaNarrativeHistorySnapshotRecord.From(ColonyDeepSnapshot.Capture(map));
            records.Add(record);
            while (records.Count > MaxRecords)
            {
                records.RemoveAt(0);
            }
        }

        public static float SimilarThreatModifier(string eventType, string incidentDef, float estimatedThreatPoints)
        {
            OrcaNarrativeHistoryRecord best = null;
            float bestDistance = 999999f;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                OrcaNarrativeHistoryRecord record = records[i];
                if (record == null || !record.captured18000 || record.outcomeLabel.NullOrEmpty())
                {
                    continue;
                }

                if (!incidentDef.NullOrEmpty() && record.incidentDef != incidentDef)
                {
                    continue;
                }

                if (!eventType.NullOrEmpty() && record.eventType != eventType)
                {
                    continue;
                }

                float distance = Mathf.Abs(record.estimatedThreatPoints - estimatedThreatPoints);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = record;
                }
            }

            if (best == null)
            {
                return 0f;
            }

            if (best.outcomeLabel == "easy")
            {
                return -12f;
            }
            if (best.outcomeLabel == "fair")
            {
                return 4f;
            }
            if (best.outcomeLabel == "difficult")
            {
                return 10f;
            }
            if (best.outcomeLabel == "disaster")
            {
                return 18f;
            }
            return 0f;
        }

        private static void CapturePost(OrcaNarrativeHistoryRecord record, ColonyDeepSnapshot snapshot, bool first)
        {
            if (first)
            {
                record.postSnapshot9000 = OrcaNarrativeHistorySnapshotRecord.From(snapshot);
                record.captured9000 = true;
            }
            else
            {
                record.postSnapshot18000 = OrcaNarrativeHistorySnapshotRecord.From(snapshot);
                record.captured18000 = true;
            }
        }

        private static void FinalizeOutcome(OrcaNarrativeHistoryRecord record)
        {
            OrcaNarrativeHistorySnapshotRecord before = record.preSnapshot;
            OrcaNarrativeHistorySnapshotRecord after = record.postSnapshot18000;
            record.deathDelta = Mathf.Max(0, before.colonists - after.colonists);
            record.downedDelta = Mathf.Max(0, after.downedColonists - before.downedColonists);
            record.mentalBreakDelta = Mathf.Max(0, after.mentalStateColonists - before.mentalStateColonists);
            record.foodDelta = after.humanEdibleNutrition - before.humanEdibleNutrition;
            record.medicineDelta = after.medicineCount - before.medicineCount;
            record.moodDelta = after.averageMood - before.averageMood;
            record.chainDamage = ChainDamage(before, after);

            if (record.deathDelta > 0 || after.medicalChain == "broken" || after.combatChain == "broken")
            {
                record.outcomeLabel = "disaster";
            }
            else if (record.downedDelta > 0 || record.mentalBreakDelta > 0 || record.moodDelta < -0.15f || after.medicalChain == "impaired")
            {
                record.outcomeLabel = "difficult";
            }
            else if (record.moodDelta < -0.05f || record.foodDelta < -2f || record.medicineDelta < 0)
            {
                record.outcomeLabel = "fair";
            }
            else
            {
                record.outcomeLabel = "easy";
            }
        }

        private static string ChainDamage(OrcaNarrativeHistorySnapshotRecord before, OrcaNarrativeHistorySnapshotRecord after)
        {
            List<string> parts = new List<string>();
            AddChainDamage(parts, "medical", before.medicalChain, after.medicalChain);
            AddChainDamage(parts, "combat", before.combatChain, after.combatChain);
            AddChainDamage(parts, "food", before.foodChain, after.foodChain);
            AddChainDamage(parts, "morale", before.moraleChain, after.moraleChain);
            return string.Join(",", parts.ToArray());
        }

        private static void AddChainDamage(List<string> parts, string label, string before, string after)
        {
            if (ChainRank(after) > ChainRank(before))
            {
                parts.Add(label + ":" + before + "->" + after);
            }
        }

        private static int ChainRank(string state)
        {
            if (state == "broken")
            {
                return 2;
            }
            if (state == "impaired")
            {
                return 1;
            }
            return 0;
        }
    }
}
