using System;

namespace DeepseekTheOrca
{
    // The planner consumes constraints, never a concrete persona's settings or mood.
    public sealed class IncidentPlanningPolicy
    {
        public readonly string instructions;
        public readonly int outputTokens;
        public readonly bool retryInvalidPlan;
        private readonly Func<OrcaIncidentCyclePlan, string> validate;

        public IncidentPlanningPolicy(string instructions, int outputTokens,
            bool retryInvalidPlan, Func<OrcaIncidentCyclePlan, string> validate)
        {
            this.instructions = instructions ?? "";
            this.outputTokens = outputTokens;
            this.retryInvalidPlan = retryInvalidPlan;
            this.validate = validate;
        }
        public bool Accepts(OrcaIncidentCyclePlan plan, out string reason)
        { reason = validate == null ? "" : validate(plan); return string.IsNullOrEmpty(reason); }
    }
}
