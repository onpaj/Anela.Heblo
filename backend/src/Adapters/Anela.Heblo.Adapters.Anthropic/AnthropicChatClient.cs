using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Adapters.Anthropic;

public class AnthropicChatClient : IChatClient
{
    private static readonly ResiliencePipeline Pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(2),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
        })
        .Build();

    private readonly AnthropicOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AnthropicChatClient> _logger;

    public AnthropicChatClient(
        IOptions<AnthropicOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<AnthropicChatClient> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.ApiKey))
            throw new InvalidOperationException("Anthropic:ApiKey is not configured.");

        var messageList = chatMessages.ToList();

        var systemMessage = messageList
            .FirstOrDefault(m => m.Role == ChatRole.System)
            ?.Text;

        var userMessages = messageList
            .Where(m => m.Role == ChatRole.User)
            .Select(m => new { role = "user", content = m.Text })
            .ToArray();

        _logger.LogDebug("Calling Claude {Model}, messages count {Count}", _options.Model, messageList.Count);

        var requestBody = BuildRequestBody(systemMessage, userMessages);

        var httpResponse = await Pipeline.ExecuteAsync(async token =>
        {
            using var client = _httpClientFactory.CreateClient("Anthropic");
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.MessagesUrl);
            request.Headers.Add("x-api-key", _options.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await client.SendAsync(request, token);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(token);
                _logger.LogError("Anthropic API error {Status}: {Body}", response.StatusCode, errorBody);
                throw new HttpRequestException($"Anthropic API returned {response.StatusCode}: {errorBody}");
            }

            return response;
        }, cancellationToken);

        var result = await httpResponse.Content.ReadFromJsonAsync<AnthropicMessagesResponse>(
            cancellationToken: cancellationToken);

        var text = result?.Content?.FirstOrDefault(b => b.Type == "text")?.Text ?? string.Empty;

        return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Streaming is not supported by AnthropicChatClient.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private object BuildRequestBody(string? systemMessage, object[] userMessages)
    {
        if (systemMessage is not null)
        {
            return new
            {
                model = _options.Model,
                max_tokens = _options.MaxTokens,
                system = systemMessage,
                messages = userMessages
            };
        }

        return new
        {
            model = _options.Model,
            max_tokens = _options.MaxTokens,
            messages = userMessages
        };
    }

    private sealed class AnthropicMessagesResponse
    {
        [JsonPropertyName("content")]
        public List<AnthropicContentBlock>? Content { get; set; }
    }

    private sealed class AnthropicContentBlock
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
