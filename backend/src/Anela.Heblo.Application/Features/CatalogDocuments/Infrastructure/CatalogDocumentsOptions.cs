using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;

public class CatalogDocumentsOptions
{
    public const string SectionName = "CatalogDocuments";

    [Required]
    public CatalogDocumentsDriveOptions Materials { get; init; } = new();

    [Required]
    public CatalogDocumentsDriveOptions PIF { get; init; } = new();
}

public class CatalogDocumentsDriveOptions
{
    [Required]
    public string DriveId { get; init; } = string.Empty;

    [Required]
    public string BasePath { get; init; } = string.Empty;
}
