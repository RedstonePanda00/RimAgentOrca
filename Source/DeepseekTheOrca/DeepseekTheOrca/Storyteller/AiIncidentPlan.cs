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
}
