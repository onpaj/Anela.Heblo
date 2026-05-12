using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Domain.Features.Article;
using Hangfire;
using Microsoft.Extensions.Logging;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate;

[AutomaticRetry(Attempts = 0)]
public sealed class GenerateArticleJob
{
    private readonly IArticleRepository _repository;
    private readonly PlanQueriesStep _planQueries;
    private readonly GatherContextStep _gatherContext;
    private readonly AggregateFactsStep _aggregateFacts;
    private readonly ValidateFactsStep _validateFacts;
    private readonly WriteArticleStep _writeArticle;
    private readonly ILogger<GenerateArticleJob> _logger;

    public GenerateArticleJob(
        IArticleRepository repository,
        PlanQueriesStep planQueries,
        GatherContextStep gatherContext,
        AggregateFactsStep aggregateFacts,
        ValidateFactsStep validateFacts,
        WriteArticleStep writeArticle,
        ILogger<GenerateArticleJob> logger)
    {
        _repository = repository;
        _planQueries = planQueries;
        _gatherContext = gatherContext;
        _aggregateFacts = aggregateFacts;
        _validateFacts = validateFacts;
        _writeArticle = writeArticle;
        _logger = logger;
    }

    public async Task RunAsync(Guid articleId, CancellationToken ct = default)
    {
        var article = await _repository.GetForUpdateAsync(articleId, ct);
        if (article == null)
        {
            _logger.LogWarning("GenerateArticleJob: article {Id} not found, skipping", articleId);
            return;
        }

        try
        {
            article.MarkAsResearching();
            await _repository.SaveChangesAsync(ct);

            var context = new ArticlePipelineContext { Article = article };
            await _planQueries.ExecuteAsync(context, ct);
            await _gatherContext.ExecuteAsync(context, ct);
            await _aggregateFacts.ExecuteAsync(context, ct);
            await _validateFacts.ExecuteAsync(context, ct);

            article.MarkAsWriting();
            await _repository.SaveChangesAsync(ct);

            await _writeArticle.ExecuteAsync(context, ct);

            article.MarkAsGenerated(context.GeneratedTitle, context.GeneratedHtml);

            foreach (var sourceRef in context.SourceRefs)
            {
                article.Sources.Add(new ArticleSource
                {
                    Id = Guid.NewGuid(),
                    ArticleId = articleId,
                    Title = sourceRef.Title,
                    Url = sourceRef.Url,
                    Type = sourceRef.Type,
                    KnowledgeBaseChunkId = sourceRef.ChunkId,
                    Confidence = sourceRef.Confidence,
                    Excerpt = sourceRef.Excerpt,
                    ValidationNote = sourceRef.ValidationNote,
                });
            }

            await _repository.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            article.MarkAsFailed("Job cancelled.");
            await _repository.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateArticleJob failed for article {Id}", articleId);
            article.MarkAsFailed(ex.Message);
            await _repository.SaveChangesAsync(CancellationToken.None);
        }
    }
}
