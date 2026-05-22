using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Services;

public class FolderSearchResult
{
    public FolderStatus Status { get; init; }
    public string FolderId { get; init; } = string.Empty;
    public string FolderName { get; init; } = string.Empty;
}
