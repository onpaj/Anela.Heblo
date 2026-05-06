using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.DeleteRoot
{
    public class DeleteRootResponse : BaseResponse
    {
        public DeleteRootResponse() : base() { }

        public DeleteRootResponse(ErrorCodes errorCode) : base(errorCode) { }
    }
}
