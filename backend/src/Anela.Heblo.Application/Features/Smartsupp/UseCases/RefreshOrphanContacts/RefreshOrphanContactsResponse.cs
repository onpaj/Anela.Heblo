using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RefreshOrphanContacts;

public class RefreshOrphanContactsResponse : BaseResponse
{
    public int Scanned { get; set; }
    public int Updated { get; set; }
    public int SkippedNoContactId { get; set; }
    public int Failed { get; set; }
    public List<string> FailedIds { get; set; } = new();

    public RefreshOrphanContactsResponse() { }
    public RefreshOrphanContactsResponse(ErrorCodes errorCode) : base(errorCode) { }
}
