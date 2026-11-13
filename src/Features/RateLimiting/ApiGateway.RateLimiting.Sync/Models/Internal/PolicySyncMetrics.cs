namespace ApiGateway.RateLimiting.Sync.Models.Internal;

public sealed class PolicySyncMetrics
{
    public string Mode { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public DateTime StartedAtUtc { get; set; }
    public DateTime FinishedAtUtc { get; set; }
    public double DurationMs { get; set; }
    public int ChangedRows { get; set; }
    public int ImpactedGroups { get; set; }
    public int WrittenGroups { get; set; }
    public int UpdatedGroups { get; set; }
    public int DeletedGroups { get; set; }
    public int SkippedGroups { get; set; }
    public int OrphanDeletedGroups { get; set; }
    public string? Watermark { get; set; }
    public string? Error { get; set; }
}
