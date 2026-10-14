using System.Text.RegularExpressions;

namespace Anela.Heblo.Domain.Features.Catalog;

/// <summary>
/// Single source of truth for the double-shot code convention. The e-shop shows each double-shot
/// as a variant product (<c>SA015030V</c>); its mirror product set (<c>SA015030S</c>) carries the
/// real composition. The codes differ only in the trailing letter. Pure naming rule — callers must
/// check the mirror actually exists in their own data. Shared by the availability sync (#4479) and
/// the order swap (#4480).
/// </summary>
public static partial class DoubleShotCodeRule
{
    private const char VariantSuffix = 'V';
    private const char SetSuffix = 'S';

    [GeneratedRegex(@"^SA\d+V$")]
    private static partial Regex VariantPattern();

    [GeneratedRegex(@"^SA\d+S$")]
    private static partial Regex SetPattern();

    public static bool IsVariantCode(string? code) =>
        !string.IsNullOrEmpty(code) && VariantPattern().IsMatch(code);

    public static bool TryGetSetCode(string? variantCode, out string setCode)
    {
        if (!IsVariantCode(variantCode))
        {
            setCode = string.Empty;
            return false;
        }

        setCode = variantCode![..^1] + SetSuffix;
        return true;
    }

    public static string ToVariantCode(string setCode)
    {
        if (string.IsNullOrEmpty(setCode) || !SetPattern().IsMatch(setCode))
            throw new ArgumentException($"'{setCode}' is not a double-shot set code (SA<digits>S).", nameof(setCode));

        return setCode[..^1] + VariantSuffix;
    }
}
