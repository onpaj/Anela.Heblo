using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Domain.Features.Attendance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Logeto;

public class LogetoClient : ILogetoClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<LogetoClient> _logger;

    public LogetoClient(
        HttpClient httpClient,
        IOptions<LogetoOptions> options,
        ILogger<LogetoClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        if (!_httpClient.DefaultRequestHeaders.Contains("AccessKey"))
        {
            _httpClient.DefaultRequestHeaders.Add("AccessKey", options.Value.AccessKey);
        }
    }

    public Task<IReadOnlyList<LogetoActivity>> GetActivitiesAsync(CancellationToken cancellationToken) =>
        GetPagedAsync<LogetoActivity>("/api/v2/Activities", baseQuery: null, cancellationToken);

    public Task<IReadOnlyList<LogetoPerson>> GetPeopleAsync(CancellationToken cancellationToken) =>
        GetPagedAsync<LogetoPerson>("/api/v2/People", baseQuery: null, cancellationToken);

    public Task<IReadOnlyList<LogetoTimeEntry>> GetTimeTrackingAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        GetPagedAsync<LogetoTimeEntry>(
            "/api/v2/TimeTracking",
            $"From={from:yyyy-MM-dd}&To={to:yyyy-MM-dd}",
            cancellationToken);

    public async Task CreateTimeEntryAsync(
        LogetoCreateTimeEntryRequest request, bool merge, CancellationToken cancellationToken)
    {
        var url = $"/api/v2/TimeTracking?merge={(merge ? "true" : "false")}";
        var response = await _httpClient.PostAsJsonAsync(url, request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private class Page<T>
    {
        public string? ContinuationToken { get; init; }
        public List<T>? Items { get; init; }
    }

    private class ErrorEnvelope
    {
        public ErrorBody? Error { get; init; }

        public class ErrorBody
        {
            public string? Code { get; init; }
            public string? Message { get; init; }
        }
    }

    private async Task<IReadOnlyList<T>> GetPagedAsync<T>(
        string path, string? baseQuery, CancellationToken cancellationToken)
    {
        var items = new List<T>();
        string? token = null;
        string? previousToken = null;

        do
        {
            var query = string.Join("&", new[]
            {
                baseQuery,
                token is null ? null : $"ContinuationToken={Uri.EscapeDataString(token)}"
            }.Where(q => !string.IsNullOrEmpty(q)));

            var url = string.IsNullOrEmpty(query) ? path : $"{path}?{query}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<Page<T>>(JsonOptions, cancellationToken)
                ?? throw new LogetoApiException(200, null, $"Logeto returned an empty body for {path}");

            items.AddRange(page.Items ?? new List<T>());

            previousToken = token;
            token = page.ContinuationToken;
        } while (!string.IsNullOrEmpty(token) && token != previousToken);

        return items;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        string? code = null;
        var message = $"Logeto API returned {(int)response.StatusCode}";

        try
        {
            var envelope = JsonSerializer.Deserialize<ErrorEnvelope>(body, JsonOptions);
            if (envelope?.Error is not null)
            {
                code = envelope.Error.Code;
                message = $"{message}: {envelope.Error.Message}";
            }
        }
        catch (JsonException)
        {
            _logger.LogWarning("Logeto error body was not valid JSON: {Body}", body);
        }

        throw new LogetoApiException((int)response.StatusCode, code, message);
    }
}
