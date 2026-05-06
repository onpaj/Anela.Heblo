using System.Collections.Generic;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetTags
{
    public class GetTagsResponse : BaseResponse
    {
        public List<TagWithCountDto> Tags { get; set; } = new();
    }
}
