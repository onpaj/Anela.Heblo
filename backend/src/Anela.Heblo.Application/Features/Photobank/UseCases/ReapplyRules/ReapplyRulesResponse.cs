using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.ReapplyRules
{
    public class ReapplyRulesResponse : BaseResponse
    {
        public int PhotosUpdated { get; set; }

        public ReapplyRulesResponse() { }
        public ReapplyRulesResponse(ErrorCodes errorCode) : base(errorCode) { }
    }
}
