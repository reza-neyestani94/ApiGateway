using ApiGateway.Shared.Contracts.Dtos.Base;
using ApiGateway.Shared.Contracts.Dtos.RateLimit;
using ApiGateway.Shared.Contracts.Exceptions;
using System.Net.Http.Json;

namespace ApiGateway.Admin.Client.Services;

public sealed class RateLimitPolicyApiClient
{
    private readonly HttpClient httpClient;

    public RateLimitPolicyApiClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<PagedResultDto<RateLimitPolicyListItemDto>> GetAllAsync(RateLimitPolicyListRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("api/RateLimit/GetAll", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<PagedResultDto<RateLimitPolicyListItemDto>>(cancellationToken);
            return result ?? PagedResultDto<RateLimitPolicyListItemDto>.Create(Array.Empty<RateLimitPolicyListItemDto>(), request.PageNumber, request.PageSize, 0);
        }
        catch (Exception ex)
        {
            throw new NotImplementedException();
        }
    }
    public async Task<RateLimitPolicyDetailsDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"api/RateLimit/GetById/{id}", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<RateLimitPolicyDetailsDto>(cancellationToken);
    }
    public async Task<RateLimitPolicyDto> CreateAsync(CreateRateLimitPolicyRequestDto request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/RateLimit/Create", request, cancellationToken);
        return await ReadResponseAsync<RateLimitPolicyDto>(response, cancellationToken);
    }
    public async Task<RateLimitPolicyDto> UpdateAsync(UpdateRateLimitPolicyRequestDto request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PutAsJsonAsync($"api/RateLimit/Update/{request.Id}", request, cancellationToken);
        return await ReadResponseAsync<RateLimitPolicyDto>(response, cancellationToken);
    }
    public async Task DeleteAsync(long id, CancellationToken cancellationToken=default)
    {
        var response = await httpClient.DeleteAsync($"api/RateLimit/Delete/{id}", cancellationToken);

        if (response.IsSuccessStatusCode)
            return;

        var errorMessage = await ReadErrorMessageAsync(response, cancellationToken);

        throw new InvalidOperationException(errorMessage);
    }



    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var message = await ReadErrorMessageAsync(response, cancellationToken);
            throw new InvalidOperationException(message);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        if (result is null)
            throw new InvalidOperationException("Server returned an empty response.");

        return result;
    }
    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var contentType = response.Content.Headers.ContentType?.MediaType;

            if (!string.IsNullOrWhiteSpace(contentType) &&
                contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                var json = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken: cancellationToken);
                if (!string.IsNullOrWhiteSpace(json?.Message))
                    return json.Message;
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }
        catch
        {
        }

        return $"Request failed with status code {(int)response.StatusCode} ({response.ReasonPhrase}).";
    }
}
