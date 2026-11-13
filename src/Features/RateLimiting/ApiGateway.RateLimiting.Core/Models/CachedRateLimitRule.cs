using ApiGateway.Shared.Domain.Enums;

namespace ApiGateway.RateLimiting.Core.Models;

public sealed class CachedRateLimitRule
{
    public int Id { get; set; }
    public string PolicyName { get; set; } = null!;
    public HttpMethodType HttpMethodType { get; set; }
    public string EndpointPattern { get; set; } = null!;
    public int LimitCount { get; set; }
    public int WindowSize { get; set; }
    public WindowUnit WindowUnit { get; set; }
    public AlgorithmType AlgorithmType { get; set; }
    public int Priority { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? Notes { get; set; }
}
