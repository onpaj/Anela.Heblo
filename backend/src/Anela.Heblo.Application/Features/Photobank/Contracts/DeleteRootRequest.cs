using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class DeleteRootRequest : IRequest<DeleteRootResponse>
    {
        public int Id { get; set; }
    }

    public class DeleteRootResponse : BaseResponse
    {
        public DeleteRootResponse() : base() { }

        public DeleteRootResponse(ErrorCodes errorCode) : base(errorCode) { }
    }
}
