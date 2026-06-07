using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListResponse : BaseResponse
{
    public static ReprintExpeditionListResponse Fail() =>
        new() { Success = false, ErrorCode = ErrorCodes.InvalidBlobPath };
}
