using ApiGateway.Shared.Domain.Enums;

namespace ApiGateway.RateLimiting.Persistence.Entities;

public sealed class RateLimitPolicy
{
    public int Id { get; set; }

    public string PolicyName { get; set; } = default!;

    public string ServiceName { get; set; } = default!;

    public HttpMethodType HttpMethodType { get; set; }

    public string EndpointPattern { get; set; } = default!;

    public SubjectType SubjectType { get; set; }

    public string SubjectKey { get; set; } = default!;

    public int LimitCount { get; set; }

    public int WindowSize { get; set; }

    public WindowUnit WindowUnit { get; set; }

    public AlgorithmType AlgorithmType { get; set; }

    public int Priority { get; set; }

    public DateTime? EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }

    public byte[] RowVersion { get; set; } = default!;
}
