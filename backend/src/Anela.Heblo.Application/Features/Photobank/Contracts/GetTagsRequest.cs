using System.Collections.Generic;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class GetTagsRequest : IRequest<GetTagsResponse>
    {
    }

    public class GetTagsResponse : BaseResponse
    {
        public List<TagWithCountDto> Tags { get; set; } = new();
    }
}
