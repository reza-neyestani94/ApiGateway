namespace ApiGateway.Shared.Contracts.Dtos.RateLimit;

public sealed class RateLimitPolicyDetailsDto
{
    public long Id { get; set; }

    public string PolicyName { get; set; } = string.Empty;

    public string ServiceName { get; set; } = string.Empty;

    public byte HttpMethodType { get; set; }

    public string EndpointPattern { get; set; } = string.Empty;

    public byte SubjectType { get; set; }

    public string SubjectKey { get; set; } = string.Empty;

    public int LimitCount { get; set; }

    public int WindowSize { get; set; }

    public byte WindowUnit { get; set; }

    public byte AlgorithmType { get; set; }

    public int Priority { get; set; }

    public DateTime? EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public bool IsActive { get; set; }

    public string? Notes { get; set; }

    public string? UpdatedBy { get; set; }
}

