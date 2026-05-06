using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.DeleteRule
{
    public class DeleteRuleResponse : BaseResponse
    {
        public DeleteRuleResponse() : base() { }

        public DeleteRuleResponse(ErrorCodes errorCode) : base(errorCode) { }
    }
}
