using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class AddRuleRequest : IRequest<AddRuleResponse>
    {
        public string PathPattern { get; set; } = null!;
        public string TagName { get; set; } = null!;
        public int SortOrder { get; set; }
    }

    public class AddRuleResponse : BaseResponse
    {
        public int Id { get; set; }
    }
}
