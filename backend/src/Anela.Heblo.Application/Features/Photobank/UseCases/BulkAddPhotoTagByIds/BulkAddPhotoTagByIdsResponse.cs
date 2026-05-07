using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTagByIds
{
    public class BulkAddPhotoTagByIdsResponse : BaseResponse
    {
        public int TagId { get; set; }
        public string TagName { get; set; } = null!;
        public int AddedCount { get; set; }
        public int AlreadyTaggedCount { get; set; }

        public BulkAddPhotoTagByIdsResponse() : base() { }
        public BulkAddPhotoTagByIdsResponse(ErrorCodes errorCode) : base(errorCode) { }
    }
}
