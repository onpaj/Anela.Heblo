using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;

public class SubmitLeafletFeedbackRequest : IRequest<SubmitLeafletFeedbackResponse>
{
    public Guid GenerationId { get; set; }

    [Range(1, 5)]
    public int PrecisionScore { get; set; }

    [Range(1, 5)]
    public int StyleScore { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }
}

public class SubmitLeafletFeedbackResponse : BaseResponse
{
    public SubmitLeafletFeedbackResponse() { }

    public SubmitLeafletFeedbackResponse(ErrorCodes errorCode, Dictionary<string, string>? details = null)
        : base(errorCode, details) { }
}
