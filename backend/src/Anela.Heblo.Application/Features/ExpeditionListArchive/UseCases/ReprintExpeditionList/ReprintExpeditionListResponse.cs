using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListResponse : BaseResponse
{
    public string? ErrorMessage { get; set; }

    public static ReprintExpeditionListResponse Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
