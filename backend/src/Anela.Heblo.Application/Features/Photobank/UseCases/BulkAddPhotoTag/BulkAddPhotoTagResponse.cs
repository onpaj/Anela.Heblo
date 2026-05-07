using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTag
{
    public class BulkAddPhotoTagResponse : BaseResponse
    {
        public int TagId { get; set; }
        public string TagName { get; set; } = null!;
        public int AddedCount { get; set; }
        public int AlreadyTaggedCount { get; set; }

        public BulkAddPhotoTagResponse() : base() { }
        public BulkAddPhotoTagResponse(ErrorCodes errorCode) : base(errorCode) { }
    }
}
