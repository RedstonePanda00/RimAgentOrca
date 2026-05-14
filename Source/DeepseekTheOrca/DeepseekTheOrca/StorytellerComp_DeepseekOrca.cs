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

            float mtbDays = Props.mtbDays;
            if (settings.planningMtbDays > 0f)
            {
                mtbDays = settings.planningMtbDays;
            }

            if (!hasPendingWork && !Rand.MTBEventOccurs(mtbDays, 60000f, 1000f))
            {
                yield break;
            }

            int lastIncidentTick;
            if (!hasPendingWork && lastIncidentTicksByTarget.TryGetValue(target.ConstantRandSeed, out lastIncidentTick))
            {
                int minSpacingTicks = GenDate.DaysToTicks(Props.minSpacingDays);
                if (Find.TickManager.TicksGame - lastIncidentTick < minSpacingTicks)
                {
                    yield break;
                }
            }

            AiToolContext context = new AiToolContext(target, this, Props);
            AiIncidentPlan plan = OrcaDecisionProvider.SelectIncidentPlan(context);
            if (plan == null && OrcaDecisionProvider.HasPendingWork)
            {
                yield break;
            }

            FiringIncident firingIncident;
            string rejectReason;
            if (!OrcaIncidentValidator.TryBuildFiringIncident(plan, context, out firingIncident, out rejectReason))
            {
                LogDebug("No valid incident plan. " + rejectReason + "\n" + context.trace);
                yield break;
            }

            lastIncidentTicksByTarget[target.ConstantRandSeed] = Find.TickManager.TicksGame;
            LogDebug("Scheduled " + firingIncident.def.defName + ". Reason: " + plan.reason + "\n" + context.trace);
            OrcaProactiveConversationManager.NotifyStorytellerIncidentScheduled(plan, firingIncident, target);
            yield return firingIncident;
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

        public override string ToString()
        {
            return base.ToString() + " (tool-driven)";
        }

        private static void LogDebug(string message)
        {
            if (DeepseekTheOrcaMod.Settings != null && DeepseekTheOrcaMod.Settings.debugLogging)
            {
                Log.Message("[Deepseek The Orca] " + message);
            }
        }
    }
}
