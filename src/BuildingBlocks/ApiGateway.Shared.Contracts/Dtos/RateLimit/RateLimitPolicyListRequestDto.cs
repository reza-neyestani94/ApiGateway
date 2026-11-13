using ApiGateway.Shared.Contracts.Dtos.Base;

namespace ApiGateway.Shared.Contracts.Dtos.RateLimit;

public sealed class RateLimitPolicyListRequestDto : PagedRequestDto
{
    public string? PolicyName { get; set; }

    public string? ServiceName { get; set; }

    public string? EndpointPattern { get; set; }

    public bool? IsActive { get; set; }

    public DateTimeOffset? CreatedAtFrom { get; set; }

    public DateTimeOffset? CreatedAtTo { get; set; }

    public DateTimeOffset? UpdatedAtFrom { get; set; }

    public DateTimeOffset? UpdatedAtTo { get; set; }
}
