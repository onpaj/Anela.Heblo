using System.Text;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared.Http;
using Anela.Heblo.Application.Shared.Json;
using Anela.Heblo.Domain.Features.Article;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class WriteArticleStep : IArticlePipelineStep
{
    private readonly IChatClient _chat;
    private readonly ArticleOptions _options;
    private readonly ILogger<WriteArticleStep> _logger;

    public WriteArticleStep(
        IChatClient chat,
        IOptions<ArticleOptions> options,
        ILogger<WriteArticleStep> logger)
    {
        _chat = chat;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)
    {
        var article = context.Article;
        var systemPrompt = BuildSystemPrompt(context.StyleGuideText);
        var userMessage = BuildUserMessage(context);

        var chatOptions = new ChatOptions
        {
            ModelId = _options.DefaultModel,
            MaxOutputTokens = _options.WriteMaxTokens
        };

        var response = await ChatRetry.RetryOnceAsync(
            () => _chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, userMessage)
                ],
                chatOptions,
                ct),
            _logger,
            ct);

        var raw = response.Text ?? string.Empty;

        var fallback = new WriteArticleOutput(article.Topic, $"<p>{raw}</p>", null);
        var parsed = JsonResponseParser.ParseOrFallback<WriteArticleOutput>(raw, fallback, _logger);

        context.GeneratedTitle = parsed.ArticleTitle ?? article.Topic;
        context.GeneratedHtml = parsed.ArticleHtml ?? $"<p>{raw}</p>";
        context.SourceRefs = MapSources(parsed.SourcesUsed, context.ContextSnippets, context.Facts);
    }

    private const string SystemInstruction =
        """
        Jsi zkušený redaktor kosmetického obsahu. Píšeš výhradně v češtině.
        Odpověz POUZE validním JSON bez markdown nebo code fences:
        {"article_title":"...","article_html":"<article>...</article>","sources_used":[{"title":"...","url":"..."}]}
        """;

    private static string BuildSystemPrompt(string? styleGuideText)
    {
        if (styleGuideText == null)
            return SystemInstruction;

        return $"STYLE GUIDE — follow this exactly:\n{styleGuideText}\n\n{SystemInstruction}";
    }

    private string BuildUserMessage(ArticlePipelineContext context)
    {
        var article = context.Article;
        var factsText = BuildFactsList(context.Facts);

        return _options.WriteArticleSystemPromptTemplate
            .Replace("{topic}", article.Topic)
            .Replace("{audience}", article.Audience ?? "obecné publikum")
            .Replace("{length}", article.Length)
            .Replace("{angle}", article.Angle ?? "(nevyspecifikováno)")
            .Replace("{facts}", factsText)
            .Replace("{style_guide}", context.StyleGuideText ?? "");
    }

    private static string BuildFactsList(List<AggregatedFact> facts)
    {
        if (facts.Count == 0)
            return "(žádná fakta)";

        var sb = new StringBuilder();
        for (var i = 0; i < facts.Count; i++)
        {
            var fact = facts[i];
            sb.Append($"{i + 1}. {fact.Claim}");

            if (fact.SourceTitle != null || fact.SourceUrl != null)
            {
                var source = fact.SourceTitle ?? fact.SourceUrl;
                sb.Append($" [zdroj: {source}]");
            }

            if (fact.ValidationNote != null)
                sb.Append($" (pozn.: {fact.ValidationNote})");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static List<(string Title, string? Url, SourceType Type, Guid? ChunkId, double? Confidence, string? Excerpt, string? ValidationNote)>
        MapSources(
            List<SourceUsedDto>? sourcesUsed,
            List<ContextSnippet> snippets,
            List<AggregatedFact> facts)
    {
        if (sourcesUsed == null)
            return [];

        return sourcesUsed.Select(s =>
        {
            var type = s.Url != null ? SourceType.Web : SourceType.KnowledgeBase;

            var snippet = snippets.FirstOrDefault(sn =>
                sn.Source == SourceType.KnowledgeBase &&
                string.Equals(sn.Title, s.Title, StringComparison.OrdinalIgnoreCase));

            var fact = facts.FirstOrDefault(f =>
                string.Equals(f.SourceTitle, s.Title, StringComparison.OrdinalIgnoreCase) ||
                (s.Url != null && string.Equals(f.SourceUrl, s.Url, StringComparison.OrdinalIgnoreCase)));

            var chunkId = type == SourceType.KnowledgeBase ? snippet?.ChunkId : null;
            var confidence = fact != null ? (double?)fact.Confidence : null;
            var excerpt = snippet?.Excerpt is { Length: > 0 } e
                ? (e.Length > 200 ? e[..200] : e)
                : null;
            var validationNote = fact?.ValidationNote;

            return (s.Title, s.Url, type, chunkId, confidence, excerpt, validationNote);
        }).ToList();
    }

    private sealed record WriteArticleOutput(
        [property: JsonPropertyName("article_title")] string? ArticleTitle,
        [property: JsonPropertyName("article_html")] string? ArticleHtml,
        [property: JsonPropertyName("sources_used")] List<SourceUsedDto>? SourcesUsed);

    private sealed record SourceUsedDto(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("url")] string? Url);
}
