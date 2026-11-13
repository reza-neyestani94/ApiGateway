using ApiGateway.Shared.Contracts.Dtos.RateLimit;

namespace ApiGateway.Admin.Client.Models.RateLimit;

public sealed class RateLimitPolicyFormModel
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
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public CreateRateLimitPolicyRequestDto ToCreateRequest()
    {
        return new CreateRateLimitPolicyRequestDto
        {
            PolicyName = PolicyName,
            ServiceName = ServiceName,
            HttpMethodType = HttpMethodType,
            EndpointPattern = EndpointPattern,
            SubjectType = SubjectType,
            SubjectKey = SubjectKey,
            LimitCount = LimitCount,
            WindowSize = WindowSize,
            WindowUnit = WindowUnit,
            AlgorithmType = AlgorithmType,
            Priority = Priority,
            EffectiveFrom = EffectiveFrom,
            EffectiveTo = EffectiveTo,
            IsActive = IsActive,
            Notes = Notes
        };
    }

    public UpdateRateLimitPolicyRequestDto ToUpdateRequest()
    {
        return new UpdateRateLimitPolicyRequestDto
        {
            Id = Id,
            PolicyName = PolicyName,
            ServiceName = ServiceName,
            HttpMethodType = HttpMethodType,
            EndpointPattern = EndpointPattern,
            SubjectType = SubjectType,
            SubjectKey = SubjectKey,
            LimitCount = LimitCount,
            WindowSize = WindowSize,
            WindowUnit = WindowUnit,
            AlgorithmType = AlgorithmType,
            Priority = Priority,
            EffectiveFrom = EffectiveFrom,
            EffectiveTo = EffectiveTo,
            IsActive = IsActive,
            Notes = Notes
        };
    }
}
