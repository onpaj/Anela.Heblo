namespace Anela.Heblo.Application.Shared.Printing;

/// <summary>
/// Encodes printable ASCII text as a Code 128 (code set B) symbol.
/// Output is a sequence of module widths alternating bar, space, bar, …,
/// starting and ending with a bar; quiet zones are left to the renderer.
/// </summary>
public static class Code128Encoder
{
    private const int StartB = 104;
    private const int Stop = 106;
    private const int ChecksumModulus = 103;
    private const char FirstEncodable = ' ';
    private const char LastEncodable = '~';

    // Bar/space widths for symbol values 0–106 (106 = stop, 7 elements).
    private static readonly string[] Patterns =
    {
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    };

    public static bool CanEncode(string? text) =>
        !string.IsNullOrEmpty(text) && text.All(c => c >= FirstEncodable && c <= LastEncodable);

    public static int ComputeChecksum(string text)
    {
        EnsureEncodable(text);

        var sum = StartB;
        for (var i = 0; i < text.Length; i++)
        {
            sum += (i + 1) * ValueOf(text[i]);
        }

        return sum % ChecksumModulus;
    }

    public static IReadOnlyList<int> EncodeModules(string text)
    {
        var symbols = new List<int> { StartB };
        symbols.AddRange(text.Select(ValueOf));
        symbols.Add(ComputeChecksum(text));
        symbols.Add(Stop);

        return symbols
            .SelectMany(symbol => Patterns[symbol].Select(width => width - '0'))
            .ToList();
    }

    private static int ValueOf(char c) => c - FirstEncodable;

    private static void EnsureEncodable(string text)
    {
        if (!CanEncode(text))
        {
            throw new ArgumentException(
                $"Text '{text}' cannot be encoded as Code 128 set B (printable ASCII only).", nameof(text));
        }
    }
}
