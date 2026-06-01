using System.Text;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared.Http;
using Anela.Heblo.Application.Shared.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class AggregateFactsStep
{
    private const int MaxSnippets = 50;

    private readonly IChatClient _chat;
    private readonly ArticleOptions _options;
    private readonly ILogger<AggregateFactsStep> _logger;
    private readonly PipelineStepRecorder _recorder;

    public AggregateFactsStep(
        IChatClient chat,
        IOptions<ArticleOptions> options,
        ILogger<AggregateFactsStep> logger,
        PipelineStepRecorder recorder)
    {
        _chat = chat;
        _options = options.Value;
        _logger = logger;
        _recorder = recorder;
    }

    public async Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)
    {
        await _recorder.RecordAsync<bool>(
            context.Article.Id,
            "AggregateFacts",
            3,
            _options.AggregateFactsModel,
            new { topic = context.Article.Topic, snippetCount = context.ContextSnippets.Count },
            async (token) =>
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
                        token),
                    _logger,
                    token);

                var raw = response.Text ?? string.Empty;
                var fallback = BuildFallback(snippets);

                var parsed = JsonResponseParser.ParseOrFallback<AggregateOutput>(raw, fallback, _logger);

                context.Facts = (parsed.Facts ?? [])
                    .Select(dto => new AggregatedFact
                    {
                        Claim = dto.Claim,
                        Confidence = dto.Confidence,
                        SourceUrl = dto.SourceUrl,
                        SourceTitle = dto.SourceTitle
                    })
                    .ToList();

                return (true, (object?)new { rawResponse = raw, facts = parsed.Facts, summary = parsed.Summary, gaps = parsed.Gaps });
            },
            ct);
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

    private static AggregateOutput BuildFallback(List<ContextSnippet> snippets)
    {
        var summary = string.Join(" ", snippets.Select(s => s.Excerpt));
        if (summary.Length > 2000)
            summary = summary[..2000];

        return new AggregateOutput(null, summary, null);
    }

    private sealed record AggregateOutput(
        [property: JsonPropertyName("facts")] List<FactDto>? Facts,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("gaps")] string? Gaps);

    private sealed record FactDto(
        [property: JsonPropertyName("claim")] string Claim,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("source_url")] string? SourceUrl,
        [property: JsonPropertyName("source_title")] string? SourceTitle);
}
