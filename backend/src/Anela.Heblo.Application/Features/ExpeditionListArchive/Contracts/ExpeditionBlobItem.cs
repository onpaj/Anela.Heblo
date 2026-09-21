namespace Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

/// <summary>
/// ExpeditionListArchive-owned metadata about a single archived expedition list blob.
/// Structurally mirrors the FileStorage module's own blob metadata type, but is owned by
/// this module so ExpeditionListArchive never references the FileStorage domain directly.
/// </summary>
public class ExpeditionBlobItem
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset? CreatedOn { get; set; }
    public long? ContentLength { get; set; }
}
