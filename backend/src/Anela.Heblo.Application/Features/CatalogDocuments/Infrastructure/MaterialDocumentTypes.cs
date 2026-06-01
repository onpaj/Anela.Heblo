namespace Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;

public static class MaterialDocumentTypes
{
    public static readonly IReadOnlyList<MaterialDocumentType> All =
    [
        new("MSDS", "Bezpečnostní list", LotRequired: false),
        new("TDS",  "Technický list",    LotRequired: false),
        new("COA",  "Certifikát analýzy", LotRequired: true),
    ];
}
