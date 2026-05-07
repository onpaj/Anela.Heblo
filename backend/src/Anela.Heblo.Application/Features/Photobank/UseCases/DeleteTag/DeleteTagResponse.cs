using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.DeleteTag
{
    public class DeleteTagResponse : BaseResponse
    {
        public DeleteTagResponse() : base() { }

        public DeleteTagResponse(ErrorCodes errorCode) : base(errorCode) { }
    }
}
