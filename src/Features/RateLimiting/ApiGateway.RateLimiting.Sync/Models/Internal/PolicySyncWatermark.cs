namespace ApiGateway.RateLimiting.Sync.Models.Internal;

public class PolicySyncWatermark
{
    public string SchemaVersion { get; set; } = null!;
    public string LastRowVersionHex { get; set; } = null!;
    public DateTime UpdatedAtUtc { get; set; }
}
