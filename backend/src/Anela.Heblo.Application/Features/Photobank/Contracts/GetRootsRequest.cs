using System.Collections.Generic;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class GetRootsRequest : IRequest<GetRootsResponse>
    {
    }

    public class GetRootsResponse : BaseResponse
    {
        public List<IndexRootDto> Roots { get; set; } = new();
    }
}
