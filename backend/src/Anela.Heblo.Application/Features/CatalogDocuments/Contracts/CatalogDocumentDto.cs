namespace Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

public class CatalogDocumentDto
{
    public string Name { get; set; } = string.Empty;
    public string WebUrl { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime ModifiedAt { get; set; }
}
