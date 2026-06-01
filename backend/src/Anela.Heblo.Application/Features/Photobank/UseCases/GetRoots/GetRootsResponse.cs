using System.Collections.Generic;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetRoots
{
    public class GetRootsResponse : BaseResponse
    {
        public List<IndexRootDto> Roots { get; set; } = new();
    }
}
