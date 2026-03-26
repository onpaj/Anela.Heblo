using System.Text.RegularExpressions;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive;

internal static class ExpeditionListArchiveConstants
{
    public const string ContainerName = "expedition-lists";

    public static readonly Regex ValidBlobPathRegex =
        new(@"^\d{4}-\d{2}-\d{2}/[^/]+\.pdf$", RegexOptions.Compiled);
}
