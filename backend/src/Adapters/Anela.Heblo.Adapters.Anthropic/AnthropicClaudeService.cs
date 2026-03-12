using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Adapters.Anthropic;

public class AnthropicClaudeService : IAnswerService
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
    private readonly ILogger<AnthropicClaudeService> _logger;

    public AnthropicClaudeService(
        IOptions<AnthropicOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<AnthropicClaudeService> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> GenerateAnswerAsync(
        string question,
        IEnumerable<string> contextChunks,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.ApiKey))
            throw new InvalidOperationException("Anthropic:ApiKey is not configured.");

        var context = string.Join("\n\n---\n\n", contextChunks);

        var prompt = $"""
            You are an expert assistant for a cosmetics manufacturing company.
            Answer the following question based strictly on the provided context.
            If the answer cannot be found in the context, say so explicitly.
            Always be precise and cite specific details from the context.

            CONTEXT:
            {context}

            QUESTION:
            {question}

            ANSWER:
            """;

        _logger.LogDebug("Calling Claude {Model}, question length {Len}", _options.Model, question.Length);

        var requestBody = new
        {
            model = _options.Model,
            max_tokens = _options.MaxTokens,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var response = await Pipeline.ExecuteAsync(async token =>
        {
            using var client = _httpClientFactory.CreateClient("Anthropic");
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.MessagesUrl);
            request.Headers.Add("x-api-key", _options.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var httpResponse = await client.SendAsync(request, token);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorBody = await httpResponse.Content.ReadAsStringAsync(token);
                _logger.LogError("Anthropic API error {Status}: {Body}", httpResponse.StatusCode, errorBody);
                throw new HttpRequestException($"Anthropic API returned {httpResponse.StatusCode}: {errorBody}");
            }

            return httpResponse;
        }, ct);

        var result = await response.Content.ReadFromJsonAsync<AnthropicMessagesResponse>(
            cancellationToken: ct);

        return result?.Content?.FirstOrDefault(b => b.Type == "text")?.Text ?? string.Empty;
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
