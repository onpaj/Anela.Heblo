using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class ReapplyRulesRequest : IRequest<ReapplyRulesResponse>
    {
    }

    public class ReapplyRulesResponse : BaseResponse
    {
        public int PhotosUpdated { get; set; }
    }
}
