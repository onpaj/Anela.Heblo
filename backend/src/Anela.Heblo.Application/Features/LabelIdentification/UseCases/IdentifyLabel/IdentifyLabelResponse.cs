using Anela.Heblo.Application.Features.LabelIdentification.Services;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;

public class IdentifyLabelResponse : BaseResponse
{
    public string RawText { get; set; } = string.Empty;
    public LabelMatchDecision Decision { get; set; }
    public List<LabelCandidateDto> Candidates { get; set; } = new();

    public IdentifyLabelResponse() { }

    public IdentifyLabelResponse(ErrorCodes errorCode) : base(errorCode) { }
}
