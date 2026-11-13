namespace ApiGateway.Shared.Contracts.Dtos.RateLimit;

public sealed class RateLimitPolicyListItemDto
{
    public int Id { get; set; }
    public string PolicyName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string EndpointPattern { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}