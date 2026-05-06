using System.Collections.Generic;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetRules
{
    public class GetRulesResponse : BaseResponse
    {
        public List<TagRuleDto> Rules { get; set; } = new();
    }
}
