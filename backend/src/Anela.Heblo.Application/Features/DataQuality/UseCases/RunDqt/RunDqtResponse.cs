using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.DataQuality.UseCases.RunDqt;

public class RunDqtResponse : BaseResponse
{
    public Guid? DqtRunId { get; set; }
}
