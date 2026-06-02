using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;

internal sealed class KnowledgeBaseArticleKnowledgeSource : IArticleKnowledgeSource
{
    private readonly IMediator _mediator;

    public KnowledgeBaseArticleKnowledgeSource(IMediator mediator) => _mediator = mediator;

    public async Task<IReadOnlyList<ArticleKnowledgeChunk>> SearchAsync(
        string query, int topK, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new SearchDocumentsRequest { Query = query, TopK = topK }, cancellationToken);

        return response.Chunks
            .Select(c => new ArticleKnowledgeChunk
            {
                ChunkId = c.ChunkId,
                SourceFilename = c.SourceFilename,
                Content = c.Content,
                Score = c.Score,
            })
            .ToArray();
    }
}
