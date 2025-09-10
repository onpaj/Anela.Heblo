using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;

public class DownloadFromUrlResponse : BaseResponse
{
    public string BlobUrl { get; set; } = null!;

    public string BlobName { get; set; } = null!;

    public string ContainerName { get; set; } = null!;

    public long FileSizeBytes { get; set; }
}