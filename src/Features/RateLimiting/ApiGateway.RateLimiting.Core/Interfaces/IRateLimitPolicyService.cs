using ApiGateway.Shared.Contracts.Dtos.Base;
using ApiGateway.Shared.Contracts.Dtos.RateLimit;

namespace ApiGateway.RateLimiting.Core.Interfaces;

public interface IRateLimitPolicyService
{
    Task<PagedResultDto<RateLimitPolicyListItemDto>> GetAllAsync(RateLimitPolicyListRequestDto request, CancellationToken cancellationToken);
    Task<RateLimitPolicyDetailsDto?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<RateLimitPolicyDto> CreateAsync(CreateRateLimitPolicyRequestDto request, CancellationToken cancellationToken);
    Task<RateLimitPolicyDto> UpdateAsync(long id,UpdateRateLimitPolicyRequestDto request, CancellationToken cancellationToken);
    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
