using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class AddPhotoTagRequest : IRequest<AddPhotoTagResponse>
    {
        public int PhotoId { get; set; }
        public string TagName { get; set; } = null!;
    }

    public class AddPhotoTagResponse : BaseResponse
    {
        public int TagId { get; set; }
        public string TagName { get; set; } = null!;

        public AddPhotoTagResponse() : base() { }

        public AddPhotoTagResponse(ErrorCodes errorCode) : base(errorCode) { }
    }
}
