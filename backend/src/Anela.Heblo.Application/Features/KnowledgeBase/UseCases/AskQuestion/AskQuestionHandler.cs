using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;
using Microsoft.Extensions.AI;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;

public class AskQuestionHandler : IRequestHandler<AskQuestionRequest, AskQuestionResponse>
{
    private const string SystemPrompt =
        "You are an expert assistant for a cosmetics manufacturing company. " +
        "Answer based strictly on the provided context. " +
        "If the answer cannot be found in the context, say so explicitly.";

    private readonly IMediator _mediator;
    private readonly IChatClient _chatClient;

    public AskQuestionHandler(IMediator mediator, IChatClient chatClient)
    {
        _mediator = mediator;
        _chatClient = chatClient;
    }

    public async Task<AskQuestionResponse> Handle(
        AskQuestionRequest request,
        CancellationToken cancellationToken)
    {
        var searchResult = await _mediator.Send(
            new SearchDocumentsRequest { Query = request.Question, TopK = request.TopK },
            cancellationToken);

        var context = string.Join("\n\n---\n\n", searchResult.Chunks.Select(c => c.Content));
        var userContent = context + "\n\n" + request.Question;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, userContent)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        var answer = response.Text ?? string.Empty;

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
