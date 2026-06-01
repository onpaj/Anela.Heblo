namespace Anela.Heblo.Application.Features.CatalogDocuments.Contracts;

public class MaterialDocumentTypeDto
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool LotRequired { get; set; }
}
