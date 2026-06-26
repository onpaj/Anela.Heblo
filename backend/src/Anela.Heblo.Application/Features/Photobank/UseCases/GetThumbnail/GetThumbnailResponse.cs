using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetThumbnail
{
    public class GetThumbnailResponse : BaseResponse
    {
        public Stream? Content { get; set; }
        public string? ContentType { get; set; }
        public long? ContentLength { get; set; }

        /// <summary>
        /// Pre-rounded retry hint (seconds). Populated only on PhotobankThumbnailThrottled.
        /// </summary>
        public int? RetryAfterSeconds { get; set; }

        public GetThumbnailResponse() : base()
        {
        }

        public GetThumbnailResponse(ErrorCodes errorCode) : base(errorCode)
        {
        }
    }
}
