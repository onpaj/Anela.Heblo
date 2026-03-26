namespace Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

public class ExpeditionListItemDto
{
    public string BlobPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset? CreatedOn { get; set; }
    public long? ContentLength { get; set; }
}
