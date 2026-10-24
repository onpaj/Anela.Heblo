using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.FileStorage.Contracts;

public class DownloadFromUrlResponse : BaseResponse
{
    public string? BlobUrl { get; set; }

    public string? BlobName { get; set; }

    public string? ContainerName { get; set; }

    public long FileSizeBytes { get; set; }
}