using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class AnthropicClaudeService : IClaudeService
{
    private readonly KnowledgeBaseOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<AnthropicClaudeService> _logger;

    public AnthropicClaudeService(
        IOptions<KnowledgeBaseOptions> options,
        IConfiguration configuration,
        ILogger<AnthropicClaudeService> logger)
    {
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
            })
            .Build();
    }

    public async Task<string> GenerateAnswerAsync(
        string question,
        IEnumerable<string> contextChunks,
        CancellationToken ct = default)
    {
        var apiKey = _configuration["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey is not configured.");

        using var api = new AnthropicApi();
        api.AuthorizeUsingApiKey(apiKey);

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

        _logger.LogDebug("Calling Claude {Model}, question length {Len}", _options.ClaudeModel, question.Length);

        var response = await _pipeline.ExecuteAsync(
            async token => await api.CreateMessageAsync(
                model: _options.ClaudeModel,
                messages: [prompt],
                maxTokens: _options.ClaudeMaxTokens,
                cancellationToken: token),
            ct);

        // response.Content is OneOf<string, IList<Block>>
        var blocks = response.Content.Value2;
        if (blocks is not null)
        {
            return blocks.OfType<TextBlock>().Select(b => b.Text).FirstOrDefault() ?? string.Empty;
        }

        return response.Content.Value1 ?? string.Empty;
    }
}
