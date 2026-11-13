using ApiGateway.Shared.Domain.Enums;

namespace ApiGateway.RateLimiting.Core.Models;

public sealed class CachedRateLimitPolicyGroup
{
    public string SchemaVersion { get; set; } = null!;
    public string Version { get; set; } = null!;
    public string ServiceName { get; set; } = null!;
    public SubjectType SubjectType { get; set; }
    public string SubjectKey { get; set; } = null!;
    public DateTime SyncedAtUtc { get; set; }
    public List<CachedRateLimitRule> Rules { get; set; } = new();
}
