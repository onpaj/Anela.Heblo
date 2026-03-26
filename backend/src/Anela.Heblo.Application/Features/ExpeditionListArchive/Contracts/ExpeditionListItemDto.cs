namespace Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

public class ExpeditionListItemDto
{
    public string FileName { get; set; } = string.Empty;
    public string BlobPath { get; set; } = string.Empty;
    public DateTimeOffset? UploadedAt { get; set; }
    public long? SizeBytes { get; set; }
}
