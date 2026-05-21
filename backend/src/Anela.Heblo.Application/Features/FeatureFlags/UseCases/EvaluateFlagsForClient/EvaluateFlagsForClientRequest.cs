using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.EvaluateFlagsForClient;

public class EvaluateFlagsForClientRequest : IRequest<EvaluateFlagsForClientResponse> { }

public class EvaluateFlagsForClientResponse : BaseResponse
{
    public Dictionary<string, bool> Flags { get; set; } = [];
}
