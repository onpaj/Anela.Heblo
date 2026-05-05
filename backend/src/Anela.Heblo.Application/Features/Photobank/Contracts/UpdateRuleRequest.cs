using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class UpdateRuleRequest : IRequest<UpdateRuleResponse>
    {
        public int Id { get; set; }
        public string PathPattern { get; set; } = null!;
        public string TagName { get; set; } = null!;
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }

    public class UpdateRuleResponse : BaseResponse
    {
        public UpdateRuleResponse() : base() { }

        public UpdateRuleResponse(ErrorCodes errorCode) : base(errorCode) { }
    }
}
