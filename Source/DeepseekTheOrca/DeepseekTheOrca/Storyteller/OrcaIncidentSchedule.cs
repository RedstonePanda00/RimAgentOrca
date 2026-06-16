using RimWorld;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaIncidentSchedule
    {
        private static OrcaIncidentCyclePlan currentCycle;
        private static int nextCycleStartTick = -1;
        private static string lastStatus = "";

        public static string LastStatus
        {
            get { return lastStatus; }
        }

        public static bool HasActiveCycle
        {
            get
            {
                return currentCycle != null
                    && Find.TickManager != null
                    && Find.TickManager.TicksGame < currentCycle.cycleEndTick;
            }
        }

        public static bool HasPendingIncidents
        {
            get { return currentCycle != null && currentCycle.HasPendingIncidents; }
        }

        public static string DebugText()
        {
            StringBuilder builder = new StringBuilder();
            int now = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            builder.AppendLine("Incident cycle schedule");
            builder.AppendLine("Current tick: " + now);
            builder.AppendLine("Decision provider available: " + OrcaDecisionProvider.IsAvailable);
            builder.AppendLine("Decision provider pending: " + OrcaDecisionProvider.HasPendingWork);
            builder.AppendLine("Decision provider status: " + (OrcaDecisionProvider.LastStatus.NullOrEmpty() ? "-" : OrcaDecisionProvider.LastStatus));
            builder.AppendLine("Last status: " + (lastStatus.NullOrEmpty() ? "-" : lastStatus));
            builder.AppendLine("Next cycle start tick: " + (nextCycleStartTick < 0 ? "-" : nextCycleStartTick.ToString()));
            builder.AppendLine();

            if (currentCycle == null)
            {
                builder.AppendLine("No active cycle plan.");
                AppendPlannerLog(builder);
                return builder.ToString();
            }

            currentCycle.Normalize();
            builder.AppendLine("Cycle target seed: " + currentCycle.targetSeed);
            builder.AppendLine("Cycle start tick: " + currentCycle.cycleStartTick + " (" + TicksFromNowText(currentCycle.cycleStartTick, now) + ")");
            builder.AppendLine("Cycle end tick: " + currentCycle.cycleEndTick + " (" + TicksFromNowText(currentCycle.cycleEndTick, now) + ")");
            builder.AppendLine("Cycle budget: " + currentCycle.cycleBudget);
            builder.AppendLine("Final remaining budget: " + currentCycle.finalRemainingBudget);
            if (!currentCycle.summary.NullOrEmpty())
            {
                builder.AppendLine("Summary: " + currentCycle.summary);
            }
            builder.AppendLine();
            builder.AppendLine("Scheduled incidents:");

            if (currentCycle.incidents == null || currentCycle.incidents.Count == 0)
            {
                builder.AppendLine("- none");
                return builder.ToString();
            }

            for (int i = 0; i < currentCycle.incidents.Count; i++)
            {
                OrcaScheduledIncidentPlan incident = currentCycle.incidents[i];
                if (incident == null)
                {
                    continue;
                }

                builder.AppendLine((i + 1) + ". " + incident.incidentDefName
                    + " | status=" + (incident.status.NullOrEmpty() ? "pending" : incident.status)
                    + " | fireTick=" + incident.fireTick
                    + " (" + TicksFromNowText(incident.fireTick, now) + ")"
                    + " | offsetDays=" + incident.offsetDays.ToString("F2")
                    + " | polarity=" + incident.polarity
                    + " | budgetDelta=" + incident.budgetDelta
                    + " | remainingBudget=" + incident.remainingBudget
                    + " | pointsFactor=" + incident.pointsFactor.ToString("F2"));
                if (!incident.reason.NullOrEmpty())
                {
                    builder.AppendLine("   reason: " + incident.reason);
                }
                if (!incident.failureReason.NullOrEmpty())
                {
                    builder.AppendLine("   failure: " + incident.failureReason);
                }
                if (!incident.debugBudgetText.NullOrEmpty())
                {
                    builder.AppendLine("   budget debug: " + incident.debugBudgetText);
                }
            }

            return builder.ToString();
        }

        private static void AppendPlannerLog(StringBuilder builder)
        {
            IEnumerable<string> logLines = OrcaDecisionProvider.LogLines;
            if (logLines == null)
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("Recent planner log:");
            int count = 0;
            foreach (string line in logLines)
            {
                if (line.NullOrEmpty())
                {
                    continue;
                }

                builder.AppendLine("- " + line);
                count++;
            }

            if (count == 0)
            {
                builder.AppendLine("- none");
            }
        }

        public static bool NeedsCyclePlan(AiToolContext context, float cycleDays)
        {
            if (context == null || context.target == null || Find.TickManager == null)
            {
                return false;
            }

            int now = Find.TickManager.TicksGame;
            if (nextCycleStartTick < 0)
            {
                nextCycleStartTick = now;
            }

            if (now < nextCycleStartTick)
            {
                return false;
            }

            if (currentCycle == null)
            {
                return true;
            }

            currentCycle.Normalize();
            if (now >= currentCycle.cycleEndTick)
            {
                return true;
            }

            return currentCycle.targetSeed != context.target.ConstantRandSeed;
        }

        public static void StoreCyclePlan(OrcaIncidentCyclePlan plan, AiToolContext context, float cycleDays)
        {
            if (plan == null || context == null || context.target == null || Find.TickManager == null)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            int cycleTicks = GenDate.DaysToTicks(cycleDays <= 0f ? 1f : cycleDays);
            plan.Normalize();
            plan.cycleStartTick = plan.cycleStartTick <= 0 ? now : plan.cycleStartTick;
            plan.cycleEndTick = plan.cycleEndTick <= plan.cycleStartTick ? plan.cycleStartTick + cycleTicks : plan.cycleEndTick;
            plan.cycleBudget = OrcaIncidentCyclePlan.DefaultCycleBudget;
            plan.targetSeed = context.target.ConstantRandSeed;

            for (int i = 0; i < plan.incidents.Count; i++)
            {
                OrcaScheduledIncidentPlan incident = plan.incidents[i];
                if (incident == null)
                {
                    continue;
                }

                incident.targetSeed = plan.targetSeed;
                incident.status = "pending";
                if (incident.fireTick <= 0)
                {
                    incident.fireTick = plan.cycleStartTick + GenDate.DaysToTicks(incident.offsetDays);
                }
                if (incident.fireTick < plan.cycleStartTick)
                {
                    incident.fireTick = plan.cycleStartTick;
                }
                if (incident.fireTick > plan.cycleEndTick)
                {
                    incident.fireTick = plan.cycleEndTick;
                }
            }

            currentCycle = plan;
            nextCycleStartTick = plan.cycleEndTick;
            SetStatus("stored cycle plan with " + plan.incidents.Count + " incident(s); next cycle tick " + nextCycleStartTick);
        }

        public static void Tick()
        {
            if (currentCycle == null || Find.TickManager == null)
            {
                return;
            }

            currentCycle.Normalize();
            int now = Find.TickManager.TicksGame;
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }

            StorytellerComp_DeepseekOrca comp = OrcaStorytellerUtility.ActiveOrcaComp();
            if (comp == null)
            {
                return;
            }

            for (int i = 0; i < currentCycle.incidents.Count; i++)
            {
                OrcaScheduledIncidentPlan incident = currentCycle.incidents[i];
                if (incident == null || !incident.IsPending || now < incident.fireTick)
                {
                    continue;
                }

                string message;
                string traceText;
                bool fired = comp.TryFireScheduledIncident(map, incident, out message, out traceText);
                if (fired)
                {
                    incident.status = "fired";
                    SetStatus(message);
                }
                else
                {
                    incident.status = "failed";
                    incident.failureReason = message ?? "";
                    SetStatus("scheduled incident failed: " + incident.failureReason);
                }

                Debug((fired ? "Scheduled incident fired: " : "Scheduled incident failed: ")
                    + incident.incidentDefName + " | " + message + "\n" + traceText);
            }

            if (now >= currentCycle.cycleEndTick && !currentCycle.HasPendingIncidents)
            {
                currentCycle = null;
            }
        }

        public static void ExposeData()
        {
            Scribe_Deep.Look(ref currentCycle, "orcaIncidentCurrentCycle");
            Scribe_Values.Look(ref nextCycleStartTick, "orcaIncidentNextCycleStartTick", -1);
            Scribe_Values.Look(ref lastStatus, "orcaIncidentScheduleLastStatus", "");
            if (currentCycle != null)
            {
                currentCycle.Normalize();
            }
        }

        private static void SetStatus(string status)
        {
            lastStatus = status ?? "";
            Debug(lastStatus);
        }

        private static string TicksFromNowText(int tick, int now)
        {
            int delta = tick - now;
            float days = delta / 60000f;
            if (delta >= 0)
            {
                return "in " + days.ToString("F2") + " days";
            }

            return (-days).ToString("F2") + " days ago";
        }

        private static void Debug(string message)
        {
            if (DeepseekTheOrcaMod.Settings != null && DeepseekTheOrcaMod.Settings.debugLogging)
            {
                Log.Message("[RimAgent] " + message);
            }
        }
    }
}
