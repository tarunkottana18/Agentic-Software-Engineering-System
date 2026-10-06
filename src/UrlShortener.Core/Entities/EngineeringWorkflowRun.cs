namespace UrlShortener.Core.Entities
{
    public sealed class EngineeringWorkflowRun
    {
        public Guid Id { get; set; }
        public string Requirement { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int PlanVersion { get; set; }
        public long Revision { get; set; }
        public string StateJson { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}