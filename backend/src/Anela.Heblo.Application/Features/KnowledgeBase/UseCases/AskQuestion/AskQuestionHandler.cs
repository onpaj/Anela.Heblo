using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;

public class AskQuestionHandler : IRequestHandler<AskQuestionRequest, AskQuestionResponse>
{
    private readonly IMediator _mediator;
    private readonly IClaudeService _claude;

    public AskQuestionHandler(IMediator mediator, IClaudeService claude)
    {
        _mediator = mediator;
        _claude = claude;
    }

    public async Task<AskQuestionResponse> Handle(
        AskQuestionRequest request,
        CancellationToken cancellationToken)
    {
        var searchResult = await _mediator.Send(
            new SearchDocumentsRequest { Query = request.Question, TopK = request.TopK },
            cancellationToken);

        var contextChunks = searchResult.Chunks.Select(c => c.Content);

        var answer = await _claude.GenerateAnswerAsync(
            request.Question,
            contextChunks,
            cancellationToken);

        return new AskQuestionResponse
        {
            Answer = answer,
            Sources = searchResult.Chunks.Select(c => new SourceReference
            {
                DocumentId = c.DocumentId,
                Filename = c.SourceFilename,
                Excerpt = c.Content[..Math.Min(200, c.Content.Length)],
                Score = c.Score
            }).ToList()
        };
    }
}
