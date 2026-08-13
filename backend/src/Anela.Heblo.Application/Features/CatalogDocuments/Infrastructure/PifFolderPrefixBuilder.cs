namespace Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;

public static class PifFolderPrefixBuilder
{
    /// <summary>
    /// Derives the PIF folder-matching prefix from a product code: the first
    /// 6 characters (or the whole code if shorter), followed by "__".
    /// </summary>
    public static string Build(string productCode)
    {
        var shortCode = productCode.Length >= 6
            ? productCode[..6]
            : productCode;
        return $"{shortCode}__";
    }
}
