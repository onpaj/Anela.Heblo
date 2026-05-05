using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class DeleteRuleRequest : IRequest<DeleteRuleResponse>
    {
        public int Id { get; set; }
    }

    public class DeleteRuleResponse : BaseResponse
    {
        public DeleteRuleResponse() : base() { }

        public DeleteRuleResponse(ErrorCodes errorCode) : base(errorCode) { }
    }
}
