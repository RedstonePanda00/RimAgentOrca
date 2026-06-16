using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class StorytellerComp_DeepseekOrca : StorytellerComp
    {
        private static readonly Dictionary<int, int> lastIncidentTicksByTarget = new Dictionary<int, int>();

        private StorytellerCompProperties_DeepseekOrca Props
        {
            get { return (StorytellerCompProperties_DeepseekOrca)props; }
        }

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            if (!OrcaDecisionProvider.IsAvailable)
            {
                yield break;
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;

            if (target == null || target.StoryState == null)
            {
                yield break;
            }

            bool hasPendingWork = OrcaDecisionProvider.HasPendingWork;
            if (!hasPendingWork && GenDate.DaysPassedSinceSettleFloat < Props.minDaysPassed)
            {
                yield break;
            }

            float cycleDays = Props.mtbDays;
            if (settings.planningMtbDays > 0f)
            {
                cycleDays = settings.planningMtbDays;
            }

            AiToolContext context = new AiToolContext(target, this, Props);
            if (!hasPendingWork && !OrcaIncidentSchedule.NeedsCyclePlan(context, cycleDays))
            {
                yield break;
            }

            OrcaIncidentCyclePlan plan = OrcaDecisionProvider.SelectIncidentCyclePlan(
                context,
                cycleDays,
                OrcaIncidentCyclePlan.DefaultCycleBudget);
            if (plan == null)
            {
                yield break;
            }

            OrcaIncidentSchedule.StoreCyclePlan(plan, context, cycleDays);
            LogDebug("Stored cycle plan with " + plan.incidents.Count + " incident(s).\n" + context.trace);
            yield break;
        }

        public bool TryFireIncidentNowForDebug(IIncidentTarget target, AiIncidentPlan plan, out string message, out string traceText)
        {
            message = "";
            traceText = "";

            if (target == null || target.StoryState == null)
            {
                message = "missing incident target";
                return false;
            }

            if (Find.Storyteller == null)
            {
                message = "no active storyteller";
                return false;
            }

            AiToolContext context = new AiToolContext(target, this, Props);
            FiringIncident firingIncident;
            string rejectReason;
            if (!OrcaIncidentValidator.TryBuildFiringIncident(plan, context, out firingIncident, out rejectReason))
            {
                traceText = context.trace.ToString();
                message = "incident rejected: " + rejectReason;
                return false;
            }

            if (!Find.Storyteller.TryFire(firingIncident))
            {
                traceText = context.trace.ToString();
                message = "Storyteller.TryFire returned false for " + firingIncident.def.defName;
                return false;
            }

            lastIncidentTicksByTarget[target.ConstantRandSeed] = Find.TickManager.TicksGame;
            traceText = context.trace.ToString();
            message = "incident fired: " + firingIncident.def.defName;
            LogDebug("Debug fired " + firingIncident.def.defName + ". Reason: " + plan.reason + "\n" + context.trace);
            return true;
        }

        public bool TryFireRaidNowForDebug(IIncidentTarget target, Dictionary<string, string> arguments, out string message, out string traceText)
        {
            message = "";
            traceText = "";

            if (target == null || target.StoryState == null)
            {
                message = "missing incident target";
                return false;
            }

            if (Find.Storyteller == null)
            {
                message = "no active storyteller";
                return false;
            }

            AiToolContext context = new AiToolContext(target, this, Props);
            bool fired = OrcaRaidUtility.TryFireRaid(context, arguments, out message, out traceText);
            if (fired)
            {
                lastIncidentTicksByTarget[target.ConstantRandSeed] = Find.TickManager.TicksGame;
                LogDebug("Debug fired raid. " + message + "\n" + traceText);
            }

            return fired;
        }

        public bool TryFireScheduledIncident(IIncidentTarget target, OrcaScheduledIncidentPlan scheduled, out string message, out string traceText)
        {
            message = "";
            traceText = "";

            if (scheduled == null)
            {
                message = "missing scheduled incident";
                return false;
            }

            if (target == null || target.StoryState == null)
            {
                message = "missing incident target";
                return false;
            }

            if (scheduled.targetSeed != 0 && scheduled.targetSeed != target.ConstantRandSeed)
            {
                message = "scheduled incident target seed mismatch";
                return false;
            }

            if (Find.Storyteller == null)
            {
                message = "no active storyteller";
                return false;
            }

            AiToolContext context = new AiToolContext(target, this, Props);
            FiringIncident firingIncident;
            string rejectReason;
            AiIncidentPlan plan = scheduled.ToIncidentPlan();
            if (!OrcaIncidentValidator.TryBuildFiringIncident(plan, context, out firingIncident, out rejectReason))
            {
                traceText = context.trace.ToString();
                message = "incident rejected: " + rejectReason;
                return false;
            }

            if (!Find.Storyteller.TryFire(firingIncident))
            {
                traceText = context.trace.ToString();
                message = "Storyteller.TryFire returned false for " + firingIncident.def.defName;
                return false;
            }

            lastIncidentTicksByTarget[target.ConstantRandSeed] = Find.TickManager.TicksGame;
            traceText = context.trace.ToString();
            message = "scheduled incident fired: " + firingIncident.def.defName;
            OrcaProactiveConversationManager.NotifyStorytellerIncidentScheduled(plan, firingIncident, target);
            LogDebug("Scheduled incident fired " + firingIncident.def.defName + ". Reason: " + plan.reason + "\n" + context.trace);
            return true;
        }

        public override string ToString()
        {
            return base.ToString() + " (tool-driven)";
        }

        private static void LogDebug(string message)
        {
            if (DeepseekTheOrcaMod.Settings != null && DeepseekTheOrcaMod.Settings.debugLogging)
            {
                Log.Message("[RimAgent] " + message);
            }
        }
    }
}
