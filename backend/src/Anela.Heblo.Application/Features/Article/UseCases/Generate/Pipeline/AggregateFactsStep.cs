using System.Text;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared.Http;
using Anela.Heblo.Application.Shared.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class AggregateFactsStep : IArticlePipelineStep
{
    private const int MaxSnippets = 50;

    private readonly IChatClient _chat;
    private readonly ArticleOptions _options;
    private readonly ILogger<AggregateFactsStep> _logger;

    public AggregateFactsStep(
        IChatClient chat,
        IOptions<ArticleOptions> options,
        ILogger<AggregateFactsStep> logger)
    {
        _chat = chat;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)
    {
        var article = context.Article;
        var snippets = context.ContextSnippets.Take(MaxSnippets).ToList();

        var userMessage = BuildUserMessage(article.Topic, article.Angle, article.Scope, snippets);

        var chatOptions = new ChatOptions
        {
            ModelId = _options.AggregateFactsModel,
            MaxOutputTokens = _options.AggregateMaxTokens
        };

        var response = await ChatRetry.RetryOnceAsync(
            () => _chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, _options.AggregateFactsSystemPrompt),
                    new ChatMessage(ChatRole.User, userMessage)
                ],
                chatOptions,
                ct),
            _logger,
            ct);

        var raw = response.Text ?? string.Empty;
        var fallback = BuildFallback(snippets);

        var parsed = JsonResponseParser.ParseOrFallback<AggregateResponse>(raw, fallback, _logger);

        context.Facts = (parsed.Facts ?? [])
            .Select(dto => new AggregatedFact
            {
                Claim = dto.Claim,
                Confidence = dto.Confidence,
                SourceUrl = dto.SourceUrl,
                SourceTitle = dto.SourceTitle
            })
            .ToList();
    }

    private static string BuildUserMessage(
        string topic,
        string? angle,
        string scope,
        List<ContextSnippet> snippets)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Téma: {topic}");
        sb.AppendLine($"Úhel: {angle ?? "(nevyspecifikováno)"}");
        sb.AppendLine($"Rozsah: {scope}");
        sb.AppendLine();
        sb.AppendLine("Zdroje:");

        for (var i = 0; i < snippets.Count; i++)
        {
            var s = snippets[i];
            sb.AppendLine($"{i + 1}. [{s.Title}] {s.Excerpt}");
        }

        return sb.ToString();
    }

    private static AggregateResponse BuildFallback(List<ContextSnippet> snippets)
    {
        var summary = string.Join(" ", snippets.Select(s => s.Excerpt));
        if (summary.Length > 2000)
            summary = summary[..2000];

        return new AggregateResponse(null, summary, null);
    }

    private sealed record AggregateResponse(
        [property: JsonPropertyName("facts")] List<FactDto>? Facts,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("gaps")] string? Gaps);

    private sealed record FactDto(
        [property: JsonPropertyName("claim")] string Claim,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("source_url")] string? SourceUrl,
        [property: JsonPropertyName("source_title")] string? SourceTitle);
}
