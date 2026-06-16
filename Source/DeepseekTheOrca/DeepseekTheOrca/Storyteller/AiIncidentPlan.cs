using System.Collections.Generic;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class AiIncidentPlan
    {
        public string incidentDefName;
        public float pointsFactor = 1f;
        public string reason;
        public string customLetterLabel;
        public string customLetterText;

        public static AiIncidentPlan For(string incidentDefName, string reason, float pointsFactor)
        {
            return new AiIncidentPlan
            {
                incidentDefName = incidentDefName,
                reason = reason,
                pointsFactor = pointsFactor
            };
        }
    }

    public sealed class OrcaScheduledIncidentPlan : IExposable
    {
        public int fireTick;
        public float offsetDays;
        public string incidentDefName = "";
        public float pointsFactor = 1f;
        public string polarity = "neutral";
        public int budgetDelta;
        public int remainingBudget;
        public string reason = "";
        public string debugBudgetText = "";
        public int targetSeed;
        public string status = "pending";
        public string failureReason = "";

        public bool IsPending
        {
            get { return status.NullOrEmpty() || status == "pending"; }
        }

        public AiIncidentPlan ToIncidentPlan()
        {
            return AiIncidentPlan.For(incidentDefName, reason ?? "AI cycle planner selected this incident.", pointsFactor);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref fireTick, "fireTick");
            Scribe_Values.Look(ref offsetDays, "offsetDays");
            Scribe_Values.Look(ref incidentDefName, "incidentDefName", "");
            Scribe_Values.Look(ref pointsFactor, "pointsFactor", 1f);
            Scribe_Values.Look(ref polarity, "polarity", "neutral");
            Scribe_Values.Look(ref budgetDelta, "budgetDelta");
            Scribe_Values.Look(ref remainingBudget, "remainingBudget");
            Scribe_Values.Look(ref reason, "reason", "");
            Scribe_Values.Look(ref debugBudgetText, "debugBudgetText", "");
            Scribe_Values.Look(ref targetSeed, "targetSeed");
            Scribe_Values.Look(ref status, "status", "pending");
            Scribe_Values.Look(ref failureReason, "failureReason", "");
        }
    }

    public sealed class OrcaIncidentCyclePlan : IExposable
    {
        public const int DefaultCycleBudget = 3;

        public int cycleStartTick;
        public int cycleEndTick;
        public int cycleBudget = DefaultCycleBudget;
        public int finalRemainingBudget;
        public int targetSeed;
        public string summary = "";
        public List<OrcaScheduledIncidentPlan> incidents = new List<OrcaScheduledIncidentPlan>();

        public bool HasPendingIncidents
        {
            get
            {
                if (incidents == null)
                {
                    return false;
                }

                for (int i = 0; i < incidents.Count; i++)
                {
                    if (incidents[i] != null && incidents[i].IsPending)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void Normalize()
        {
            if (incidents == null)
            {
                incidents = new List<OrcaScheduledIncidentPlan>();
            }
            if (cycleBudget <= 0)
            {
                cycleBudget = DefaultCycleBudget;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref cycleStartTick, "cycleStartTick");
            Scribe_Values.Look(ref cycleEndTick, "cycleEndTick");
            Scribe_Values.Look(ref cycleBudget, "cycleBudget", DefaultCycleBudget);
            Scribe_Values.Look(ref finalRemainingBudget, "finalRemainingBudget");
            Scribe_Values.Look(ref targetSeed, "targetSeed");
            Scribe_Values.Look(ref summary, "summary", "");
            Scribe_Collections.Look(ref incidents, "incidents", LookMode.Deep);
            Normalize();
        }
    }
}
