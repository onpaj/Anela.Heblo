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
    private readonly PipelineStepRecorder _recorder;

    public WriteArticleStep(
        IChatClient chat,
        IOptions<ArticleOptions> options,
        ILogger<WriteArticleStep> logger,
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
            "WriteArticle",
            5,
            _options.DefaultModel,
            new { topic = context.Article.Topic, factCount = context.Facts.Count, styleGuideLength = context.StyleGuideText?.Length },
            async (token) =>
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
                        token),
                    _logger,
                    token);

                var raw = response.Text ?? string.Empty;

                var fallback = new WriteArticleOutput(article.Topic, $"<p>{raw}</p>", null);
                var parsed = JsonResponseParser.ParseOrFallback<WriteArticleOutput>(raw, fallback, _logger);

                context.GeneratedTitle = parsed.ArticleTitle ?? article.Topic;
                context.GeneratedHtml = parsed.ArticleHtml ?? $"<p>{raw}</p>";
                context.SourceRefs = MapSources(parsed.SourcesUsed, context.ContextSnippets, context.Facts);

                return (true, (object?)new { rawResponse = raw, articleTitle = parsed.ArticleTitle, sourcesUsed = parsed.SourcesUsed });
            },
            ct);
    }

    private const string SystemInstruction =
        """
        Jsi zkušený redaktor kosmetického obsahu. Píšeš výhradně v češtině.
        Odpověz POUZE validním JSON bez markdown nebo code fences.
        V poli article_html použij výhradně HTML tagy – nikdy nepište doslovný text "\n" jako obsah.
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

    private static List<ArticleSourceRef> MapSources(
        List<SourceUsedDto>? sourcesUsed,
        List<ContextSnippet> snippets,
        List<AggregatedFact> facts)
    {
        if (sourcesUsed == null)
            return [];

        return sourcesUsed.Select(source =>
        {
            var snippetMatch = snippets.FirstOrDefault(s =>
                string.Equals(s.Title, source.Title, StringComparison.OrdinalIgnoreCase));
            var factMatch = facts.FirstOrDefault(f =>
                string.Equals(f.SourceTitle, source.Title, StringComparison.OrdinalIgnoreCase));

            return new ArticleSourceRef(
                Title: source.Title,
                Url: source.Url,
                Type: source.Url != null ? SourceType.Web : SourceType.KnowledgeBase,
                ChunkId: snippetMatch?.ChunkId,
                Confidence: snippetMatch?.Score,
                Excerpt: TruncateExcerpt(factMatch?.Claim),
                ValidationNote: factMatch?.ValidationNote);
        }).ToList();
    }

    private static string? TruncateExcerpt(string? claim)
    {
        if (string.IsNullOrEmpty(claim))
            return null;
        return claim.Length <= 200 ? claim : claim[..200];
    }

    private sealed record WriteArticleOutput(
        [property: JsonPropertyName("article_title")] string? ArticleTitle,
        [property: JsonPropertyName("article_html")] string? ArticleHtml,
        [property: JsonPropertyName("sources_used")] List<SourceUsedDto>? SourcesUsed);

    private sealed record SourceUsedDto(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("url")] string? Url);
}
