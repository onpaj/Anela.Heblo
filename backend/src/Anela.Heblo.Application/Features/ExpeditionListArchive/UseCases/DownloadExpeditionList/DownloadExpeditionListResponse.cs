using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;

public class DownloadExpeditionListResponse : BaseResponse
{
    public Stream? Stream { get; set; }
    public string ContentType { get; set; } = "application/pdf";
    public string FileName { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }

    public static DownloadExpeditionListResponse Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
