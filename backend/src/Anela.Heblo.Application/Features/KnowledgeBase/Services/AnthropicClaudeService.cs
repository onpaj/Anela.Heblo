using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class AnthropicClaudeService : IClaudeService
{
    private readonly IConfiguration _configuration;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly ILogger<AnthropicClaudeService> _logger;

    public AnthropicClaudeService(IConfiguration configuration, ILogger<AnthropicClaudeService> logger)
    {
        _configuration = configuration;
        _model = configuration["KnowledgeBase:ClaudeModel"] ?? "claude-sonnet-4-6";
        _maxTokens = int.TryParse(configuration["KnowledgeBase:ClaudeMaxTokens"], out var t) ? t : 1024;
        _logger = logger;
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

        _logger.LogDebug("Calling Claude {Model}, question length {Len}", _model, question.Length);

        var response = await api.CreateMessageAsync(
            model: _model,
            messages: [prompt],
            maxTokens: _maxTokens,
            cancellationToken: ct);

        // response.Content is OneOf<string, IList<Block>>
        var blocks = response.Content.Value2;
        if (blocks is not null)
        {
            return blocks.OfType<TextBlock>().Select(b => b.Text).FirstOrDefault() ?? string.Empty;
        }

        return response.Content.Value1 ?? string.Empty;
    }
}
