using ApiGateway.RateLimiting.Core.Exceptions;
using ApiGateway.RateLimiting.Core.Interfaces;
using ApiGateway.RateLimiting.Persistence.Entities;
using ApiGateway.Shared.Contracts.Dtos.Base;
using ApiGateway.Shared.Contracts.Dtos.RateLimit;
using ApiGateway.Shared.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApiGateway.RateLimiting.Persistence.Services;

public class RateLimitPolicyService : IRateLimitPolicyService
{
    private readonly RateLimitingDbContext dbContext;

    public RateLimitPolicyService(RateLimitingDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public async Task<PagedResultDto<RateLimitPolicyListItemDto>> GetAllAsync(RateLimitPolicyListRequestDto request, CancellationToken cancellationToken)
    {
        var query = dbContext.RateLimitPolicies.AsNoTracking().Where(p=>p.IsDeleted == false).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.PolicyName))
        {
            var policyName = request.PolicyName.Trim();
            query = query.Where(x => x.PolicyName.Contains(policyName));
        }

        if (!string.IsNullOrWhiteSpace(request.ServiceName))
        {
            var serviceName = request.ServiceName.Trim();
            query = query.Where(x => x.ServiceName.Contains(serviceName));
        }

        if (!string.IsNullOrWhiteSpace(request.EndpointPattern))
        {
            var endpointPattern = request.EndpointPattern.Trim();
            query = query.Where(x => x.EndpointPattern.Contains(endpointPattern));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => x.IsActive == request.IsActive.Value);
        }

        if (request.CreatedAtFrom.HasValue)
        {
            query = query.Where(x => x.CreatedAt >= request.CreatedAtFrom.Value);
        }

        if (request.CreatedAtTo.HasValue)
        {
            query = query.Where(x => x.CreatedAt <= request.CreatedAtTo.Value);
        }

        if (request.UpdatedAtFrom.HasValue)
        {
            query = query.Where(x => x.UpdatedAt >= request.UpdatedAtFrom.Value);
        }

        if (request.UpdatedAtTo.HasValue)
        {
            query = query.Where(x => x.UpdatedAt <= request.UpdatedAtTo.Value);
        }

        var totalItems = await query.CountAsync(cancellationToken);

        query = ApplySorting(query, request.SortBy, request.SortDirection);

        var skip = (request.PageNumber - 1) * request.PageSize;
        var items = await query
        .Skip(skip)
        .Take(request.PageSize)
        .Select(x => new RateLimitPolicyListItemDto
        {
            Id = x.Id,
            PolicyName = x.PolicyName,
            ServiceName = x.ServiceName,
            EndpointPattern = x.EndpointPattern,
            IsActive = x.IsActive
        })
        .ToListAsync(cancellationToken);

        return PagedResultDto<RateLimitPolicyListItemDto>.Create(items, request.PageNumber, request.PageSize, totalItems);
    }
    public async Task<RateLimitPolicyDetailsDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        return await dbContext.RateLimitPolicies
            .Where(x => x.Id == id && x.IsDeleted == false)
            .Select(x => new RateLimitPolicyDetailsDto
            {
                Id = x.Id,
                PolicyName = x.PolicyName,
                ServiceName = x.ServiceName,
                HttpMethodType = (byte)x.HttpMethodType,
                EndpointPattern = x.EndpointPattern,
                SubjectType = (byte)x.SubjectType,
                SubjectKey = x.SubjectKey,
                LimitCount = x.LimitCount,
                WindowSize = x.WindowSize,
                WindowUnit = (byte)x.WindowUnit,
                AlgorithmType = (byte)x.AlgorithmType,
                Priority = x.Priority,
                EffectiveFrom = x.EffectiveFrom,
                EffectiveTo = x.EffectiveTo,
                IsActive = x.IsActive,
                Notes = x.Notes,
                UpdatedBy = x.UpdatedBy
            })
            .FirstOrDefaultAsync(cancellationToken);
    }
    public async Task<RateLimitPolicyDto> CreateAsync(CreateRateLimitPolicyRequestDto request, CancellationToken cancellationToken)
    {
        var normalized = ValidateAndNormalizeForCreate(request);

        await EnsureNoDuplicatePolicyAsync(
            excludingId: null,
            serviceName: normalized.ServiceName,
            httpMethodType: request.HttpMethodType,
            endpointPattern: normalized.EndpointPattern,
            subjectType: request.SubjectType,
            subjectKey: normalized.SubjectKey,
            windowUnit: request.WindowUnit,
            windowSize: request.WindowSize,
            cancellationToken: cancellationToken);

        var entity = new RateLimitPolicy
        {
            PolicyName = normalized.PolicyName,
            ServiceName = normalized.ServiceName,
            HttpMethodType = (HttpMethodType)request.HttpMethodType,
            EndpointPattern = normalized.EndpointPattern,
            SubjectType = (SubjectType)request.SubjectType,
            SubjectKey = normalized.SubjectKey,
            LimitCount = request.LimitCount,
            WindowSize = request.WindowSize,
            WindowUnit = (WindowUnit)request.WindowUnit,
            AlgorithmType = (AlgorithmType)request.AlgorithmType,
            Priority = request.Priority,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            IsActive = request.IsActive,
            IsDeleted = false,
            Notes = normalized.Notes
        };

        dbContext.RateLimitPolicies.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicatePolicyViolation(ex))
        {
            throw new ConflictException(
                "Another policy already exists with the same service, method, endpoint, subject, window unit and window size.");
        }
        catch (DbUpdateException ex) when (IsTriggerViolation(ex))
        {
            throw new BusinessRuleException(
                "ServiceName, SubjectType and SubjectKey are immutable. Deactivate/delete old policy and insert a new one.");
        }

        return MapToDto(entity);
    }
    public async Task<RateLimitPolicyDto> UpdateAsync(long id, UpdateRateLimitPolicyRequestDto request, CancellationToken cancellationToken)
    {
        if (id != request.Id)
            throw new ArgumentException("Route id and body id do not match.");

        if (request.Id <= 0)
            throw new ArgumentException("Policy id is invalid.");

        var entity = await dbContext.RateLimitPolicies
            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken);

        if (entity is null)
            throw new InvalidOperationException($"Rate limit policy with id '{request.Id}' was not found.");

        var normalized = ValidateAndNormalizeForUpdate(request);

        EnsureImmutableFieldsNotChanged(entity, normalized.ServiceName, request.SubjectType, normalized.SubjectKey);

        await EnsureNoDuplicatePolicyAsync(
            excludingId: request.Id,
            serviceName: normalized.ServiceName,
            httpMethodType: request.HttpMethodType,
            endpointPattern: normalized.EndpointPattern,
            subjectType: request.SubjectType,
            subjectKey: normalized.SubjectKey,
            windowUnit: request.WindowUnit,
            windowSize: request.WindowSize,
            cancellationToken: cancellationToken);

        entity.PolicyName = normalized.PolicyName;
        entity.HttpMethodType = (HttpMethodType)request.HttpMethodType;
        entity.EndpointPattern = normalized.EndpointPattern;
        entity.LimitCount = request.LimitCount;
        entity.WindowSize = request.WindowSize;
        entity.WindowUnit = (WindowUnit)request.WindowUnit;
        entity.AlgorithmType = (AlgorithmType)request.AlgorithmType;
        entity.Priority = request.Priority;
        entity.EffectiveFrom = request.EffectiveFrom;
        entity.EffectiveTo = request.EffectiveTo;
        entity.IsActive = request.IsActive;
        entity.Notes = normalized.Notes;
        entity.UpdatedAt = DateTime.UtcNow;
        // TODO
        // After Connect To SSO Get Username From Token
        entity.UpdatedBy = "TestUsername";

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicatePolicyViolation(ex))
        {
            throw new ConflictException(
                "Another policy already exists with the same service, method, endpoint, subject, window unit and window size.");
        }
        catch (DbUpdateException ex) when (IsTriggerViolation(ex))
        {
            throw new BusinessRuleException(
                "ServiceName, SubjectType and SubjectKey are immutable. Deactivate/delete old policy and insert a new one.");
        }

        return MapToDto(entity);
    }
    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var policy = await dbContext.RateLimitPolicies.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);

        if (policy is null)
            throw new InvalidOperationException("Policy not found.");

        policy.IsDeleted = true;
        policy.IsActive = false;
        policy.UpdatedAt = DateTime.UtcNow;
        policy.UpdatedBy = "system";

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // Helper

    private static NormalizedPolicyInput ValidateAndNormalizeForCreate(CreateRateLimitPolicyRequestDto request)
    {
        return ValidateAndNormalize(
            policyName: request.PolicyName,
            serviceName: request.ServiceName,
            httpMethodType: request.HttpMethodType,
            endpointPattern: request.EndpointPattern,
            subjectType: request.SubjectType,
            subjectKey: request.SubjectKey,
            limitCount: request.LimitCount,
            windowSize: request.WindowSize,
            windowUnit: request.WindowUnit,
            algorithmType: request.AlgorithmType,
            effectiveFrom: request.EffectiveFrom,
            effectiveTo: request.EffectiveTo,
            notes: request.Notes);
    }
    private static NormalizedPolicyInput ValidateAndNormalizeForUpdate(UpdateRateLimitPolicyRequestDto request)
    {
        return ValidateAndNormalize(
            policyName: request.PolicyName,
            serviceName: request.ServiceName,
            httpMethodType: request.HttpMethodType,
            endpointPattern: request.EndpointPattern,
            subjectType: request.SubjectType,
            subjectKey: request.SubjectKey,
            limitCount: request.LimitCount,
            windowSize: request.WindowSize,
            windowUnit: request.WindowUnit,
            algorithmType: request.AlgorithmType,
            effectiveFrom: request.EffectiveFrom,
            effectiveTo: request.EffectiveTo,
            notes: request.Notes);
    }
    private static NormalizedPolicyInput ValidateAndNormalize(string policyName, string serviceName, byte httpMethodType, string endpointPattern, byte subjectType, string? subjectKey, int limitCount, int windowSize, byte windowUnit, byte algorithmType, DateTime? effectiveFrom, DateTime? effectiveTo, string? notes)
    {
        if (!Enum.IsDefined(typeof(HttpMethodType), httpMethodType))
            throw new ArgumentException("Invalid HTTP method.");

        if (!Enum.IsDefined(typeof(SubjectType), subjectType))
            throw new ArgumentException("Invalid subject type.");

        if (!Enum.IsDefined(typeof(WindowUnit), windowUnit))
            throw new ArgumentException("Invalid window unit.");

        if (!Enum.IsDefined(typeof(AlgorithmType), algorithmType))
            throw new ArgumentException("Invalid algorithm type.");

        if (string.IsNullOrWhiteSpace(policyName))
            throw new ArgumentException("Policy name is required.");

        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name is required.");

        if (string.IsNullOrWhiteSpace(endpointPattern))
            throw new ArgumentException("Endpoint pattern is required.");

        if (limitCount <= 0)
            throw new ArgumentException("Limit count must be greater than zero.");

        if (windowSize <= 0)
            throw new ArgumentException("Window size must be greater than zero.");

        if (effectiveFrom.HasValue && effectiveTo.HasValue && effectiveTo.Value <= effectiveFrom.Value)
            throw new ArgumentException("Effective To must be later than Effective From.");

        var normalizedPolicyName = policyName.Trim();
        var normalizedServiceName = serviceName.Trim();
        var normalizedEndpointPattern = endpointPattern.Trim();
        var normalizedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        if (normalizedPolicyName.Length > 200)
            throw new ArgumentException("Policy name cannot be longer than 200 characters.");

        if (normalizedServiceName.Length > 150)
            throw new ArgumentException("Service name cannot be longer than 150 characters.");

        if (normalizedEndpointPattern.Length > 500)
            throw new ArgumentException("Endpoint pattern cannot be longer than 500 characters.");

        if (normalizedNotes is not null && normalizedNotes.Length > 1000)
            throw new ArgumentException("Notes cannot be longer than 1000 characters.");

        //if (normalizedUpdatedBy is not null && normalizedUpdatedBy.Length > 150)
        //    throw new ArgumentException("UpdatedBy cannot be longer than 150 characters.");

        var parsedSubjectType = (SubjectType)subjectType;
        string normalizedSubjectKey;

        if (parsedSubjectType == SubjectType.Global)
        {
            normalizedSubjectKey = "*";
        }
        else
        {
            normalizedSubjectKey = subjectKey?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(normalizedSubjectKey))
                throw new ArgumentException("Subject key is required for non-global subject types.");

            if (normalizedSubjectKey == "*")
                throw new ArgumentException("Subject key cannot be '*' for non-global subject types.");
        }

        if (normalizedSubjectKey.Length > 200)
            throw new ArgumentException("Subject key cannot be longer than 200 characters.");

        return new NormalizedPolicyInput(
            PolicyName: normalizedPolicyName,
            ServiceName: normalizedServiceName,
            EndpointPattern: normalizedEndpointPattern,
            SubjectKey: normalizedSubjectKey,
            Notes: normalizedNotes);
    }
    private static void EnsureImmutableFieldsNotChanged(RateLimitPolicy entity, string newServiceName, byte newSubjectType, string newSubjectKey)
    {
        if (!string.Equals(entity.ServiceName, newServiceName, StringComparison.Ordinal) ||
            entity.SubjectType != (SubjectType)newSubjectType ||
            !string.Equals(entity.SubjectKey, newSubjectKey, StringComparison.Ordinal))
        {
            throw new BusinessRuleException(
                "ServiceName, SubjectType and SubjectKey are immutable. Deactivate/delete old policy and insert a new one.");
        }
    }
    private async Task EnsureNoDuplicatePolicyAsync(long? excludingId, string serviceName, byte httpMethodType, string endpointPattern, byte subjectType, string subjectKey, byte windowUnit, int windowSize, CancellationToken cancellationToken)
    {
        var exists = await dbContext.RateLimitPolicies.AnyAsync(x =>
            !x.IsDeleted &&
            (!excludingId.HasValue || x.Id != excludingId.Value) &&
            x.ServiceName == serviceName &&
            x.HttpMethodType == (HttpMethodType)httpMethodType &&
            x.EndpointPattern == endpointPattern &&
            x.SubjectType == (SubjectType)subjectType &&
            x.SubjectKey == subjectKey &&
            x.WindowUnit == (WindowUnit)windowUnit &&
            x.WindowSize == windowSize,
            cancellationToken);

        if (exists)
        {
            throw new ConflictException(
                "Another policy already exists with the same service, method, endpoint, subject, window unit and window size.");
        }
    }
    private static bool IsDuplicatePolicyViolation(DbUpdateException ex)
    {
        if (ex.InnerException is SqlException sqlEx)
        {
            return (sqlEx.Number == 2601 || sqlEx.Number == 2627) &&
                   sqlEx.Message.Contains("UX_RateLimitPolicies_ActivePolicy", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
    private static bool IsTriggerViolation(DbUpdateException ex)
    {
        if (ex.InnerException is SqlException sqlEx)
        {
            return sqlEx.Number == 51000 &&
                   sqlEx.Message.Contains(
                       "ServiceName, SubjectType and SubjectKey are immutable",
                       StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
    private static RateLimitPolicyDto MapToDto(RateLimitPolicy entity)
    {
        return new RateLimitPolicyDto
        {
            Id = entity.Id,
            PolicyName = entity.PolicyName,
            ServiceName = entity.ServiceName,
            HttpMethodType = (byte)entity.HttpMethodType,
            EndpointPattern = entity.EndpointPattern,
            SubjectType = (byte)entity.SubjectType,
            SubjectKey = entity.SubjectKey,
            LimitCount = entity.LimitCount,
            WindowSize = entity.WindowSize,
            WindowUnit = (byte)entity.WindowUnit,
            AlgorithmType = (byte)entity.AlgorithmType,
            Priority = entity.Priority,
            EffectiveFrom = entity.EffectiveFrom,
            EffectiveTo = entity.EffectiveTo,
            IsActive = entity.IsActive,
            Notes = entity.Notes,
            UpdatedBy = entity.UpdatedBy
        };
    }
    private sealed record NormalizedPolicyKey(string ServiceName, string EndpointPattern, string SubjectKey);
    private sealed record NormalizedPolicyInput(string PolicyName, string ServiceName, string EndpointPattern, string SubjectKey, string? Notes);
    private static IQueryable<RateLimitPolicy> ApplySorting(IQueryable<RateLimitPolicy> query, string? sortBy, SortDirectionDto sortDirection)
    {
        var isDescending = sortDirection switch
        {
            SortDirectionDto.Asc => false,
            SortDirectionDto.Desc => true,
            _ => true
        };

        var normalizedSortBy = sortBy?.Trim().ToLowerInvariant();

        return normalizedSortBy switch
        {
            "createdate" => isDescending
                ? query.OrderByDescending(x => x.CreatedAt)
                : query.OrderBy(x => x.CreatedAt),

            "updatedate" => isDescending
                ? query.OrderByDescending(x => x.UpdatedAt)
                : query.OrderBy(x => x.UpdatedAt),

            "policyname" => isDescending
                ? query.OrderByDescending(x => x.PolicyName)
                : query.OrderBy(x => x.PolicyName),

            "servicename" => isDescending
                ? query.OrderByDescending(x => x.ServiceName)
                : query.OrderBy(x => x.ServiceName),
            
            _ => query.OrderByDescending(x => x.UpdatedAt)
        };
    }


}