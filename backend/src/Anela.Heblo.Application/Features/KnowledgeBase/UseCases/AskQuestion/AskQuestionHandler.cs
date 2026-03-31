using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;

public class AskQuestionHandler : IRequestHandler<AskQuestionRequest, AskQuestionResponse>
{
    private readonly IMediator _mediator;
    private readonly IChatClient _chatClient;
    private readonly KnowledgeBaseOptions _options;

    public AskQuestionHandler(IMediator mediator, IChatClient chatClient, IOptions<KnowledgeBaseOptions> options)
    {
        _mediator = mediator;
        _chatClient = chatClient;
        _options = options.Value;
    }

    public async Task<AskQuestionResponse> Handle(
        AskQuestionRequest request,
        CancellationToken cancellationToken)
    {
        var searchResult = await _mediator.Send(
            new SearchDocumentsRequest { Query = request.Question, TopK = request.TopK },
            cancellationToken);

        if (!searchResult.Chunks.Any())
        {
            return new AskQuestionResponse
            {
                Answer = "V dostupných dokumentech jsem nenašel relevantní informaci k vaší otázce.",
                Sources = []
            };
        }

        var context = string.Join("\n\n---\n\n", searchResult.Chunks.Select(c => c.Content));
        var systemPrompt = _options.AskQuestionSystemPrompt
            .Replace("{context}", context)
            .Replace("{query}", request.Question);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, request.Question)
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
