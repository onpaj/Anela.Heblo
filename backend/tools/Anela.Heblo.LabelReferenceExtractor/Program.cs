using System.Text.Json;
using Anela.Heblo.Application.Features.LabelIdentification.Services;
using UglyToad.PdfPig;

// Usage: dotnet run --project backend/tools/Anela.Heblo.LabelReferenceExtractor -- <pdfDir> <outputJson>
if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: <pdfDir> <outputJson>");
    return 1;
}

var pdfDir = args[0];
var outputPath = args[1];

var pdfs = Directory.GetFiles(pdfDir, "*.pdf").OrderBy(p => p).ToList();
if (pdfs.Count == 0)
{
    Console.Error.WriteLine($"No PDFs found in {pdfDir}");
    return 1;
}

var extracted = new List<(string Code, string Normalized)>();
var failures = new List<string>();

foreach (var path in pdfs)
{
    var code = Path.GetFileNameWithoutExtension(path);
    try
    {
        using var document = PdfDocument.Open(path);
        var rawText = string.Join("\n", document.GetPages().Select(p => p.Text));
        var normalized = LabelTextNormalizer.Normalize(rawText);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            failures.Add($"{code}: no extractable text layer");
            continue;
        }

        extracted.Add((code, normalized));
    }
    catch (Exception ex)
    {
        failures.Add($"{code}: {ex.Message}");
    }
}

// Group by family = first 6 characters of the product code (KRE005015 -> KRE005).
// Where a family's sizes differ in text (only KRE003 in the current corpus), the
// LONGER text is the representative: it is the superset in practice and
// token_set_ratio is insensitive to the extra tokens.
var entries = extracted
    .GroupBy(e => e.Code[..6])
    .OrderBy(g => g.Key, StringComparer.Ordinal)
    .Select(g => new
    {
        family = g.Key,
        codes = g.Select(e => e.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray(),
        normalized = g.OrderByDescending(e => e.Normalized.Length).First().Normalized,
    })
    .ToList();

var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, json);

Console.WriteLine($"{extracted.Count} labels -> {entries.Count} families -> {outputPath}");
foreach (var failure in failures)
{
    Console.Error.WriteLine($"FAILED {failure}");
}

return failures.Count == 0 ? 0 : 2;
