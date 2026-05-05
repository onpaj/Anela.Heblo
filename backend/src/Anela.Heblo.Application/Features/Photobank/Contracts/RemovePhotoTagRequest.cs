using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class RemovePhotoTagRequest : IRequest<RemovePhotoTagResponse>
    {
        public int PhotoId { get; set; }
        public int TagId { get; set; }
    }

    public class RemovePhotoTagResponse : BaseResponse
    {
        public RemovePhotoTagResponse() : base() { }

        public RemovePhotoTagResponse(ErrorCodes errorCode) : base(errorCode) { }
    }
}
