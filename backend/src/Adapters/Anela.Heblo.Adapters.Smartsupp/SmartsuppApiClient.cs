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
                _logger.LogError("Smartsupp contact failed {Status}: {Body}", response.StatusCode, errorBody);
                var ex = new HttpRequestException($"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
                if (response.Headers.RetryAfter?.Delta is { } delta)
                    ex.Data["RetryAfter"] = delta;
                throw ex;
            }

            var raw = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<SmartsuppContactApiResponse>(raw, JsonOptions);

            return result is null ? null : MapContact(result);
        }, cancellationToken);
    }

    public async Task<SmartsuppConversationData?> GetConversationAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.ApiToken))
            throw new InvalidOperationException("Smartsupp:ApiToken is not configured.");

        return await _pipeline.ExecuteAsync(async ct =>
        {
            var client = _httpClientFactory.CreateClient("Smartsupp");
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_options.BaseUrl}conversations/{conversationId}");
            request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");

            var response = await client.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Smartsupp get conversation failed {Status}: {Body}", response.StatusCode, errorBody);
                var ex = new HttpRequestException($"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
                if (response.Headers.RetryAfter?.Delta is { } delta)
                    ex.Data["RetryAfter"] = delta;
                throw ex;
            }

            var raw = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<SmartsuppConversationApiItem>(raw, JsonOptions);

            return result is null ? null : MapConversation(result);
        }, cancellationToken);
    }

    public async Task<SmartsuppVisitorData?> GetVisitorAsync(
        string visitorId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.ApiToken))
            throw new InvalidOperationException("Smartsupp:ApiToken is not configured.");

        return await _pipeline.ExecuteAsync(async ct =>
        {
            var client = _httpClientFactory.CreateClient("Smartsupp");
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_options.BaseUrl}visitors/{visitorId}");
            request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");

            var response = await client.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Smartsupp visitor failed {Status}: {Body}", response.StatusCode, errorBody);
                var ex = new HttpRequestException($"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
                if (response.Headers.RetryAfter?.Delta is { } delta)
                    ex.Data["RetryAfter"] = delta;
                throw ex;
            }

            var raw = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<SmartsuppVisitorApiResponse>(raw, JsonOptions);
            return result is null ? null : MapVisitor(result);
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
            ExtId = item.ExtId,
            Status = item.Status ?? "open",
            Unread = item.Unread,
            CreatedAt = Unspecified(item.CreatedAt),
            UpdatedAt = Unspecified(item.UpdatedAt),
            FinishedAt = item.FinishedAt is { } fa ? Unspecified(fa) : null,
            ContactId = item.ContactId,
            VisitorId = item.VisitorId,
            AgentIds = item.AgentIds ?? new List<string>(),
            AssignedIds = item.AssignedIds ?? new List<string>(),
            GroupId = item.GroupId,
            RatingValue = item.RatingValue,
            RatingText = item.RatingText,
            Domain = item.Domain,
            Referer = item.Referer,
            IsOffline = item.IsOffline,
            IsServed = item.IsServed,
            ChannelType = item.Channel?.Type,
            ChannelId = item.Channel?.Id,
            LocationCountry = item.Location?.Country,
            LocationCity = item.Location?.City,
            LocationIp = item.Location?.Ip,
            LocationCode = item.Location?.Code,
            VariablesJson = item.Variables is { } v ? JsonSerializer.Serialize(v) : null,
            TagsJson = item.Tags is { } t ? JsonSerializer.Serialize(t) : null,
            LastMessageText = item.LastMessage?.Text,
            LastMessageAt = item.LastMessage?.CreatedAt is { } lm ? Unspecified(lm) : null,
        };

    private static SmartsuppMessageData MapMessage(SmartsuppMessageApiItem item) =>
        new()
        {
            Id = item.Id ?? "",
            ExtId = item.ExtId,
            Type = item.Type,
            SubType = item.SubType,
            Content = item.Content?.Text,
            ContentType = item.Content?.Type,
            CreatedAt = Unspecified(item.CreatedAt),
            UpdatedAt = item.UpdatedAt is { } ua ? Unspecified(ua) : default,
            ConversationId = item.ConversationId,
            VisitorId = item.VisitorId,
            AgentId = item.AgentId,
            TriggerId = item.TriggerId,
            TriggerName = item.TriggerName,
            DeliveryTo = item.DeliveryTo,
            DeliveryStatus = item.DeliveryStatus,
            DeliveredAt = item.DeliveredAt is { } da ? Unspecified(da) : null,
            IsReply = item.IsReply,
            IsFirstReply = item.IsFirstReply,
            IsOffline = item.IsOffline,
            IsOfflineReply = item.IsOfflineReply,
            ResponseTime = item.ResponseTime,
            PageUrl = item.PageUrl,
            AttachmentsJson = item.Attachments is { } a ? JsonSerializer.Serialize(a) : null,
            ChannelType = item.Channel?.Type,
            ChannelId = item.Channel?.Id,
        };

    private static SmartsuppVisitorData MapVisitor(SmartsuppVisitorApiResponse item) =>
        new()
        {
            Id = item.Id ?? "",
            UserAgent = item.UserAgent,
            Os = item.Os,
            Browser = item.Browser,
            BrowserVersion = item.BrowserVersion,
            VisitsCount = item.Visits,
        };

    private static SmartsuppContactData MapContact(SmartsuppContactApiResponse item) =>
        new()
        {
            Id = item.Id ?? "",
            CreatedAt = Unspecified(item.CreatedAt),
            UpdatedAt = Unspecified(item.UpdatedAt),
            Email = item.Email,
            Name = item.Name,
            Phone = item.Phone,
            Note = item.Note,
            BannedAt = item.BannedAt is { } ba ? Unspecified(ba) : null,
            BannedBy = item.BannedBy,
            GdprApproved = item.GdprApproved,
            TagsJson = item.Tags is { } tg ? JsonSerializer.Serialize(tg) : null,
            PropertiesJson = item.Properties is { } p ? JsonSerializer.Serialize(p) : null,
        };

    private static DateTime Unspecified(DateTime dt) =>
        DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);

    // ---- API request shapes ----

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

    // ---- API response shapes ----

    private sealed class SmartsuppSearchApiResponse
    {
        public int Total { get; set; }
        public JsonElement[]? After { get; set; }
        public List<SmartsuppConversationApiItem>? Items { get; set; }
    }

    private sealed class SmartsuppConversationApiItem
    {
        public string? Id { get; set; }
        public string? ExtId { get; set; }
        public string? Status { get; set; }
        public bool Unread { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public SmartsuppChannelApiItem? Channel { get; set; }
        public string? ContactId { get; set; }
        public string? VisitorId { get; set; }
        public List<string>? AgentIds { get; set; }
        public List<string>? AssignedIds { get; set; }
        public string? GroupId { get; set; }
        public int? RatingValue { get; set; }
        public string? RatingText { get; set; }
        public string? Domain { get; set; }
        public string? Referer { get; set; }
        public bool IsOffline { get; set; }
        public bool IsServed { get; set; }
        public JsonElement? Variables { get; set; }
        public JsonElement? Tags { get; set; }
        public SmartsuppLocationApiItem? Location { get; set; }
        public SmartsuppLastMessageApiItem? LastMessage { get; set; }
    }

    private sealed class SmartsuppChannelApiItem
    {
        public string? Type { get; set; }
        public string? Id { get; set; }
    }

    private sealed class SmartsuppLocationApiItem
    {
        public string? Ip { get; set; }
        public string? Code { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
    }

    private sealed class SmartsuppLastMessageApiItem
    {
        public string? Text { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    private sealed class SmartsuppMessagesApiResponse
    {
        public int Total { get; set; }
        public string? After { get; set; }
        public List<SmartsuppMessageApiItem>? Items { get; set; }
    }

    private sealed class SmartsuppMessageApiItem
    {
        public string? Id { get; set; }
        public string? ExtId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? Type { get; set; }
        public string? SubType { get; set; }
        public SmartsuppChannelApiItem? Channel { get; set; }
        public string? ConversationId { get; set; }
        public string? VisitorId { get; set; }
        public string? AgentId { get; set; }
        public string? TriggerId { get; set; }
        public string? TriggerName { get; set; }
        public string? DeliveryTo { get; set; }
        public string? DeliveryStatus { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public bool IsReply { get; set; }
        public bool IsFirstReply { get; set; }
        public bool IsOffline { get; set; }
        public bool IsOfflineReply { get; set; }
        public int? ResponseTime { get; set; }
        public JsonElement? Attachments { get; set; }
        public string? PageUrl { get; set; }
        public SmartsuppMessageContentApiItem? Content { get; set; }
    }

    private sealed class SmartsuppMessageContentApiItem
    {
        public string? Type { get; set; }
        public string? Text { get; set; }
        public JsonElement? Data { get; set; }
    }

    private sealed class SmartsuppContactApiResponse
    {
        public string? Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? Email { get; set; }
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public JsonElement? Properties { get; set; }
        public string? Note { get; set; }
        public DateTime? BannedAt { get; set; }
        public string? BannedBy { get; set; }
        public JsonElement? Tags { get; set; }
        public bool GdprApproved { get; set; }
    }

    private sealed class SmartsuppVisitorApiResponse
    {
        public string? Id { get; set; }
        public string? UserAgent { get; set; }
        public string? Os { get; set; }
        public string? Browser { get; set; }
        public string? BrowserVersion { get; set; }
        public int? Visits { get; set; }
    }

    private sealed class SmartsuppAgentsApiResponse
    {
        public List<SmartsuppAgentApiItem>? Agents { get; set; }
    }

    private sealed class SmartsuppAgentApiItem
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
    }

    private sealed class SendMessageApiRequest
    {
        public SendMessageApiContent Content { get; init; } = null!;
        public string? AgentId { get; init; }
    }

    private sealed class SendMessageApiContent
    {
        public string Type { get; init; } = "text";
        public string Text { get; init; } = null!;
    }

    private sealed class SendMessageApiResponse
    {
        public string? Id { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public async Task<IReadOnlyList<SmartsuppAgentData>> GetAgentsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.ApiToken))
            throw new InvalidOperationException("Smartsupp:ApiToken is not configured.");

        return await _pipeline.ExecuteAsync(async ct =>
        {
            var client = _httpClientFactory.CreateClient("Smartsupp");
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.BaseUrl}agents");
            request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");

            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Smartsupp get agents failed {Status}: {Body}", response.StatusCode, errorBody);
                throw new HttpRequestException($"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
            }

            var raw = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<SmartsuppAgentsApiResponse>(raw, JsonOptions);

            return (result?.Agents ?? [])
                .Where(a => a.Id is not null)
                .Select(a => new SmartsuppAgentData { Id = a.Id!, Name = a.Name, Email = a.Email })
                .ToList();
        }, cancellationToken);
    }

    public async Task<SmartsuppSentMessageData> SendMessageAsync(
        string conversationId,
        string content,
        string? agentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.ApiToken))
            throw new InvalidOperationException("Smartsupp:ApiToken is not configured.");

        // Agent attribution: when agentId is provided, Smartsupp records the message
        // as sent by that agent (and renders their profile name to the customer).
        // When null, the message is attributed to the API token's default sender.
        // Do NOT include an `agent` block; doing so triggers sub_type=agent and 422
        // ("agent_id is required when sub_type is \"agent\"") even if agent_id is set.
        var body = new SendMessageApiRequest
        {
            Content = new SendMessageApiContent { Text = content },
            AgentId = string.IsNullOrWhiteSpace(agentId) ? null : agentId,
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);

        return await _pipeline.ExecuteAsync(async ct =>
        {
            var client = _httpClientFactory.CreateClient("Smartsupp");
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_options.BaseUrl}conversations/{conversationId}/messages");
            request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Smartsupp send message failed {Status}: {Body}",
                    response.StatusCode, errorBody);
                var ex = new HttpRequestException(
                    $"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
                if (response.Headers.RetryAfter?.Delta is { } delta)
                    ex.Data["RetryAfter"] = delta;
                throw ex;
            }

            var raw = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<SendMessageApiResponse>(raw, JsonOptions)
                ?? throw new InvalidOperationException("Smartsupp send message returned an empty response body.");

            return new SmartsuppSentMessageData
            {
                Id = result.Id ?? string.Empty,
                CreatedAt = DateTime.SpecifyKind(result.CreatedAt, DateTimeKind.Unspecified),
            };
        }, cancellationToken);
    }
}
