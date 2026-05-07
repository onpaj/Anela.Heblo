using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.AddPhotoTag
{
    public class AddPhotoTagResponse : BaseResponse
    {
        public int TagId { get; set; }
        public string TagName { get; set; } = null!;

        public AddPhotoTagResponse() : base() { }

        public AddPhotoTagResponse(ErrorCodes errorCode) : base(errorCode) { }
    }
}
