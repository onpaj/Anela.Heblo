using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.UpdateRule
{
    public class UpdateRuleResponse : BaseResponse
    {
        public UpdateRuleResponse() : base() { }

        public UpdateRuleResponse(ErrorCodes errorCode) : base(errorCode) { }
    }
}
