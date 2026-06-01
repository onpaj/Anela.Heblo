namespace Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;

public static class MaterialFilenameBuilder
{
    /// <summary>
    /// Builds a structured Material filename: {TYPE}__{lot}__{commonName}{ext}
    /// When lot is empty, the separator is preserved: {TYPE}____{commonName}{ext}
    /// </summary>
    public static string Build(string typeCode, string lot, string commonName, string originalExtension)
    {
        var ext = originalExtension.Length > 0 && !originalExtension.StartsWith('.')
            ? $".{originalExtension}"
            : originalExtension;

        return $"{typeCode}__{lot}__{commonName.Trim()}{ext}";
    }
}
