using ApiGateway.RateLimiting.Core.Exceptions;
using ApiGateway.RateLimiting.Core.Interfaces;
using ApiGateway.Shared.Contracts.Dtos.RateLimit;
using ApiGateway.Shared.Contracts.Exceptions;

namespace ApiGateway.RateLimiting.Admin.Api.Endpoints;

public static class RateLimitEndpoints
{
    public static IEndpointRouteBuilder MapRateLimitEndpoints(this IEndpointRouteBuilder route)
    {
        var rateLimitGroup = route.MapGroup("/api/RateLimit")
            .WithTags("RateLimit");

        rateLimitGroup.MapGet("/GetById/{id}", GetById).WithName("GetById")
            .WithSummary("Get By Id")
            .WithDescription("Get By Id");

        rateLimitGroup.MapPost("/GetAll", GetAllRateLimitPolicies).WithName("GetAllRateLimit")
            .WithSummary("Get all rate limiting policies from db")
            .WithDescription("Get all rate limiting policies from db");

        rateLimitGroup.MapPost("/Create", CreatePolicy).WithName("CreatePolicy")
            .WithSummary("Create Policy")
            .WithDescription("Create Policy");

        rateLimitGroup.MapPut("/Update/{id}", UpdatePolicy).WithName("UpdatePolicy")
            .WithSummary("Update Policy")
            .WithDescription("Update Policy");

        rateLimitGroup.MapDelete("/Delete/{id}", DeletePolicy).WithName("DeletePolicy")
            .WithSummary("Delete Policy")
            .WithDescription("Delete Policy");

        return route;
    }

    // Handlers

    private static async Task<IResult> GetById(long id, IRateLimitPolicyService rateLimitPolicyService, CancellationToken cancellationToken)
    {
        var result = await rateLimitPolicyService.GetByIdAsync(id, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
    private static async Task<IResult> GetAllRateLimitPolicies(RateLimitPolicyListRequestDto request, IRateLimitPolicyService rateLimitPolicyService, CancellationToken cancellationToken)
    {
        var policies = await rateLimitPolicyService.GetAllAsync(request, cancellationToken);

        return Results.Ok(policies);
    }
    private static async Task<IResult> CreatePolicy(CreateRateLimitPolicyRequestDto request, IRateLimitPolicyService rateLimitPolicyService, CancellationToken cancellationToken)
    {
        try
        {
            var result = await rateLimitPolicyService.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/rate-limit-policies/{result.Id}", result);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (BusinessRuleException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.InternalServerError(new { message = ex.Message });
        }
    }
    private static async Task<IResult> UpdatePolicy(long id, UpdateRateLimitPolicyRequestDto request, IRateLimitPolicyService rateLimitPolicyService, CancellationToken cancellationToken)
    {
        try
        {
            var result = await rateLimitPolicyService.UpdateAsync(id, request, cancellationToken);
            return Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (BusinessRuleException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
    }

    private static async Task<IResult> DeletePolicy(long id, IRateLimitPolicyService rateLimitPolicyService, CancellationToken cancellationToken)
    {
        try
        {
            await rateLimitPolicyService.DeleteAsync(id, cancellationToken);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new ApiErrorResponse
            {
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return Results.InternalServerError(ex.Message);
        }
    }

}
