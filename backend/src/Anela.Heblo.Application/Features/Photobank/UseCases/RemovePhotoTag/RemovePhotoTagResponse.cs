using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.RemovePhotoTag
{
    public class RemovePhotoTagResponse : BaseResponse
    {
        public RemovePhotoTagResponse() : base() { }

        public RemovePhotoTagResponse(ErrorCodes errorCode) : base(errorCode) { }
    }
}
