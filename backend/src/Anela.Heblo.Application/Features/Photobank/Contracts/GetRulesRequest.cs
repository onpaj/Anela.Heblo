using System.Collections.Generic;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class GetRulesRequest : IRequest<GetRulesResponse>
    {
    }

    public class GetRulesResponse : BaseResponse
    {
        public List<TagRuleDto> Rules { get; set; } = new();
    }
}
