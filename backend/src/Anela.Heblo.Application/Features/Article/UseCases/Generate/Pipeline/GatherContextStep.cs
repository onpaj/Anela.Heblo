using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Shared.WebSearch;
using Anela.Heblo.Domain.Features.Article;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class GatherContextStep : IArticlePipelineStep
{
    private readonly IMediator _mediator;
    private readonly IWebSearchClient _webSearch;
    private readonly IArticleStyleGuideSource _styleGuideSource;
    private readonly ArticleOptions _options;
    private readonly ILogger<GatherContextStep> _logger;
    private readonly PipelineStepRecorder _recorder;

    public GatherContextStep(
        IMediator mediator,
        IWebSearchClient webSearch,
        IArticleStyleGuideSource styleGuideSource,
        IOptions<ArticleOptions> options,
        ILogger<GatherContextStep> logger,
        PipelineStepRecorder recorder)
    {
        _mediator = mediator;
        _webSearch = webSearch;
        _styleGuideSource = styleGuideSource;
        _options = options.Value;
        _logger = logger;
        _recorder = recorder;
    }

    public async Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)
    {
        await _recorder.RecordAsync<bool>(
            context.Article.Id,
            "GatherContext",
            2,
            null,
            new { queries = context.SearchQueries },
            async (token) =>
            {
                var article = context.Article;

                var kbTask = article.UsedKnowledgeBase
                    ? GatherKnowledgeBaseSnippetsAsync(context.SearchQueries, token)
                    : Task.FromResult<List<ContextSnippet>>([]);

                var webTask = article.UsedWebSearch
                    ? GatherWebSnippetsAsync(context.SearchQueries, token)
                    : Task.FromResult<List<ContextSnippet>>([]);

                var styleGuideTask = HasStyleGuide(article)
                    ? LoadStyleGuideAsync(article, token)
                    : Task.FromResult<string?>(null);

                await Task.WhenAll(kbTask, webTask, styleGuideTask);

                var kbSnippets = kbTask.Result;
                var webSnippets = webTask.Result;
                var styleGuideText = styleGuideTask.Result;

                var deduplicatedWeb = DeduplicateByUrl(webSnippets);

                context.ContextSnippets = [.. kbSnippets, .. deduplicatedWeb];
                context.StyleGuideText = styleGuideText;

                var allSnippets = kbSnippets.Concat(webSnippets).ToList();
                return (true, (object?)new { snippets = allSnippets, styleGuideLength = styleGuideText?.Length });
            },
            ct);
    }

    private async Task<List<ContextSnippet>> GatherKnowledgeBaseSnippetsAsync(
        List<string> queries,
        CancellationToken ct)
    {
        var snippets = new List<ContextSnippet>();

        foreach (var query in queries)
        {
            try
            {
                var response = await _mediator.Send(
                    new SearchDocumentsRequest { Query = query, TopK = _options.KnowledgeBaseTopK },
                    ct);

                snippets.AddRange(response.Chunks.Select(chunk => new ContextSnippet
                {
                    Source = SourceType.KnowledgeBase,
                    Title = chunk.SourceFilename,
                    Excerpt = chunk.Content,
                    Url = null,
                    ChunkId = chunk.ChunkId,
                    Score = chunk.Score
                }));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "KB search failed for query '{Query}'", query);
            }
        }

        return snippets;
    }

    private async Task<List<ContextSnippet>> GatherWebSnippetsAsync(
        List<string> queries,
        CancellationToken ct)
    {
        var snippets = new List<ContextSnippet>();

        foreach (var query in queries)
        {
            try
            {
                var result = await _webSearch.SearchAsync(
                    query,
                    new WebSearchOptions { Locale = "cs", Geo = "cz", Top = _options.WebSearchTopK },
                    ct);

                snippets.AddRange(result.Hits.Select(hit => new ContextSnippet
                {
                    Source = SourceType.Web,
                    Title = hit.Title,
                    Excerpt = hit.Snippet,
                    Url = hit.Url
                }));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Web search failed for query '{Query}'", query);
            }
        }

        return snippets;
    }

    private async Task<string?> LoadStyleGuideAsync(DomainArticle article, CancellationToken ct)
    {
        try
        {
            return await _styleGuideSource.DownloadStyleGuideTextAsync(
                article.StyleGuideDriveId!,
                article.StyleGuideItemPath!,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load style guide from '{Path}'", article.StyleGuideItemPath);
            return null;
        }
    }

    private static bool HasStyleGuide(DomainArticle article) =>
        article.StyleGuideDriveId != null && article.StyleGuideItemPath != null;

    private static List<ContextSnippet> DeduplicateByUrl(List<ContextSnippet> snippets)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ContextSnippet>();

        foreach (var snippet in snippets)
        {
            if (snippet.Url == null || seen.Add(snippet.Url))
                result.Add(snippet);
        }

        return result;
    }
}
