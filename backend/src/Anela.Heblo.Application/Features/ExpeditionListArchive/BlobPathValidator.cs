using System.Text.RegularExpressions;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive;

internal static class BlobPathValidator
{
    private static readonly Regex ValidBlobPathPattern =
        new(@"^\d{4}-\d{2}-\d{2}/[^/]+\.pdf$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsValid(string blobPath)
    {
        if (string.IsNullOrWhiteSpace(blobPath))
            return false;

        if (blobPath.Contains(".."))
            return false;

        if (!ValidBlobPathPattern.IsMatch(blobPath))
            return false;

        var datePart = blobPath.Split('/')[0];
        return DateOnly.TryParseExact(datePart, "yyyy-MM-dd", out _);
    }
}
