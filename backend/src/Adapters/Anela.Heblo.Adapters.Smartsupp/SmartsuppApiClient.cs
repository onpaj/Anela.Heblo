using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Adapters.Smartsupp;

public class SmartsuppApiClient : ISmartsuppApiClient
{
    private static readonly ResiliencePipeline DefaultPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(2),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>(ex => ex.StatusCode == HttpStatusCode.TooManyRequests),
            DelayGenerator = static args =>
            {
                if (args.Outcome.Exception is HttpRequestException { Data: var data } &&
                    data["RetryAfter"] is TimeSpan retryAfter)
                    return new ValueTask<TimeSpan?>(retryAfter);
                return new ValueTask<TimeSpan?>((TimeSpan?)null);
            }
        })
        .Build();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SmartsuppOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SmartsuppApiClient> _logger;
    private readonly ResiliencePipeline _pipeline;

    public SmartsuppApiClient(
        IOptions<SmartsuppOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<SmartsuppApiClient> logger,
        ResiliencePipeline? pipeline = null)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _pipeline = pipeline ?? DefaultPipeline;
    }

    public async Task<SmartsuppSearchResult> SearchConversationsAsync(
        string? cursor,
        int size,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.ApiToken))
            throw new InvalidOperationException("Smartsupp:ApiToken is not configured.");

        var body = new ConversationSearchRequest
        {
            Size = size,
            Query = [new ConversationQueryItem { Field = "status", Value = ["open", "served"] }],
            Sort = [new ConversationSortItem()],
            After = cursor is not null ? JsonSerializer.Deserialize<JsonElement[]>(cursor) : null,
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);

        return await _pipeline.ExecuteAsync(async ct =>
        {
            var client = _httpClientFactory.CreateClient("Smartsupp");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}conversations/search");
            request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Smartsupp search failed {Status}: {Body}", response.StatusCode, errorBody);
                var ex = new HttpRequestException($"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
                if (response.Headers.RetryAfter?.Delta is { } delta)
                    ex.Data["RetryAfter"] = delta;
                throw ex;
            }

            var raw = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<SmartsuppSearchApiResponse>(raw, JsonOptions)
                         ?? new SmartsuppSearchApiResponse();

            return MapSearchResult(result);
        }, cancellationToken);
    }

    public async Task<List<SmartsuppMessageData>> GetConversationMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.ApiToken))
            throw new InvalidOperationException("Smartsupp:ApiToken is not configured.");

        return await _pipeline.ExecuteAsync(async ct =>
        {
            var client = _httpClientFactory.CreateClient("Smartsupp");
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_options.BaseUrl}conversations/{conversationId}/messages?size=200");
            request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");

            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Smartsupp messages failed {Status}: {Body}", response.StatusCode, errorBody);
                throw new HttpRequestException($"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
            }

            var raw = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<SmartsuppMessagesApiResponse>(raw, JsonOptions);

            return result?.Items?.Select(MapMessage).ToList() ?? new List<SmartsuppMessageData>();
        }, cancellationToken);
    }

    public async Task<SmartsuppContactData?> GetContactAsync(
        string contactId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.ApiToken))
            throw new InvalidOperationException("Smartsupp:ApiToken is not configured.");

        return await _pipeline.ExecuteAsync(async ct =>
        {
            var client = _httpClientFactory.CreateClient("Smartsupp");
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_options.BaseUrl}contacts/{contactId}");
            request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");

            var response = await client.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Smartsupp GetContact failed {Status}: {Body}", response.StatusCode, errorBody);
                throw new HttpRequestException($"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
            }

            var raw = await response.Content.ReadAsStringAsync(ct);
            var item = JsonSerializer.Deserialize<SmartsuppContactApiItem>(raw, JsonOptions);
            if (item is null)
                return null;

            return new SmartsuppContactData
            {
                Id = item.Id ?? contactId,
                CreatedAt = Unspecified(item.CreatedAt),
                UpdatedAt = Unspecified(item.UpdatedAt),
                Email = item.Email,
                Name = item.Name,
                Phone = item.Phone,
                Note = item.Note,
            };
        }, cancellationToken);
    }

    private static SmartsuppSearchResult MapSearchResult(SmartsuppSearchApiResponse r) =>
        new()
        {
            Total = r.Total,
            After = r.After is not null ? JsonSerializer.Serialize(r.After) : null,
            Items = r.Items?.Select(MapConversation).ToList() ?? new List<SmartsuppConversationData>()
        };

    private static SmartsuppConversationData MapConversation(SmartsuppConversationApiItem item) =>
        new()
        {
            Id = item.Id ?? "",
            Status = item.Status ?? "open",
            Unread = item.Unread,
            CreatedAt = Unspecified(item.CreatedAt),
            UpdatedAt = Unspecified(item.UpdatedAt),
            ContactName = item.Contact?.Name,
            ContactEmail = item.Contact?.Email,
            ContactAvatarUrl = item.Contact?.AvatarUrl,
            LastMessageText = item.LastMessage?.Text,
            LastMessageAt = item.LastMessage?.CreatedAt is { } lm ? Unspecified(lm) : null,
        };

    private static SmartsuppMessageData MapMessage(SmartsuppMessageApiItem item) =>
        new()
        {
            Id = item.Id ?? "",
            AuthorType = item.Author?.Type ?? "visitor",
            AuthorName = item.Author?.Name,
            Content = item.Content?.Text,
            CreatedAt = Unspecified(item.CreatedAt),
        };

    private static DateTime Unspecified(DateTime dt) =>
        DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);

    // ---- API request shapes (private, internal to adapter) ----

    private sealed class ConversationSearchRequest
    {
        public int Size { get; init; }
        public List<ConversationQueryItem> Query { get; init; } = [];
        public List<ConversationSortItem> Sort { get; init; } = [];
        public JsonElement[]? After { get; init; }
    }

    private sealed class ConversationQueryItem
    {
        public string Field { get; init; } = "";
        public string[] Value { get; init; } = [];
    }

    private sealed class ConversationSortItem
    {
        public string CreatedAt { get; init; } = "desc";
    }

    // ---- API response shapes (private, internal to adapter) ----

    private sealed class SmartsuppSearchApiResponse
    {
        public int Total { get; set; }
        public JsonElement[]? After { get; set; }
        public List<SmartsuppConversationApiItem>? Items { get; set; }
    }

    private sealed class SmartsuppConversationApiItem
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public bool Unread { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public SmartsuppContactApiItem? Contact { get; set; }
        public SmartsuppLastMessageApiItem? LastMessage { get; set; }
    }

    private sealed class SmartsuppContactApiItem
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Phone { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class SmartsuppLastMessageApiItem
    {
        public string? Text { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    private sealed class SmartsuppMessagesApiResponse
    {
        public List<SmartsuppMessageApiItem>? Items { get; set; }
    }

    private sealed class SmartsuppMessageApiItem
    {
        public string? Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public SmartsuppMessageAuthorApiItem? Author { get; set; }
        public SmartsuppMessageContentApiItem? Content { get; set; }
    }

    private sealed class SmartsuppMessageAuthorApiItem
    {
        public string? Type { get; set; }
        public string? Name { get; set; }
    }

    private sealed class SmartsuppMessageContentApiItem
    {
        public string? Text { get; set; }
    }
}
