using Anela.Heblo.Application.Features.FeatureFlags.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.ListFlags;

public class ListFlagsRequest : IRequest<ListFlagsResponse> { }

public class ListFlagsResponse : BaseResponse
{
    public List<FlagStatusDto> Flags { get; set; } = [];
}
