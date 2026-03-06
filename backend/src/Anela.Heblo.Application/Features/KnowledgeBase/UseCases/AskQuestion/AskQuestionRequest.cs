using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;

public class AskQuestionRequest : IRequest<AskQuestionResponse>
{
    public string Question { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
}

public class AskQuestionResponse : BaseResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<SourceReference> Sources { get; set; } = [];
}

public class SourceReference
{
    public Guid DocumentId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public double Score { get; set; }
}
