# Label Identification in the Terminal — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Photograph a product etiquette in the Heblo terminal and get the product code and name back.

**Architecture:** A `LabelIdentification` vertical slice in `Anela.Heblo.Application` (pure normalizer + pure matcher + an OCR service behind an interface), one controller, and one terminal workflow. Reference data is 25 family entries extracted from artwork PDFs once, offline, and committed as an embedded JSON resource — nothing parses a PDF at request time.

**Tech Stack:** .NET 8, MediatR, FluentValidation, xUnit + Moq + FluentAssertions, FuzzySharp (new), PdfPig 0.1.9 (existing), SkiaSharp 2.88.3 (existing), React 18 + TypeScript, TanStack Query, Tailwind.

**Spec:** `docs/superpowers/specs/2026-08-03-label-identification-terminal-design.md`

## Global Constraints

- **DTOs are classes, never C# records.** The OpenAPI generator mishandles record parameter order.
- **Every Application `*Response` must inherit `BaseResponse`.** A reflection contract test fails in CI otherwise.
- **Validators are registered manually** per-module alongside `ValidationBehavior<TRequest, TResponse>`. There is no `AddValidatorsFromAssembly` in this codebase.
- **Frontend API calls go through the generated typed client.** Never `(apiClient as any).http.fetch`.
- **Frontend URLs are absolute:** `${apiClient.baseUrl}${relativeUrl}`. A relative URL hits port 3001 instead of 5001.
- **Frontend tests run with `react-scripts test`**, not `npx jest` (TS parse errors).
- **Frontend build gate is `CI=false npm run build`**, not `npx tsc --noEmit` (tsc false-greens on react-i18next `.d.ts` parse errors).
- **Regenerate the TS client with** `dotnet msbuild -t:GenerateFrontendClientManual` (automatic generation is disabled).
- All user-facing copy is **Czech**.
- Scoring weights: `0.7 × token_set_ratio + 0.3 × (jaccard × 100)`. Thresholds: `AutoConfirmScore = 90`, `AutoConfirmMargin = 5`, `LowConfidenceFloor = 60`. All named constants, overridable via options.
- Family = **first 6 characters** of the product code (`KRE005015` → `KRE005`).
- Solution file is at repo root: `Anela.Heblo.sln`.
- `dotnet test` hangs when another Conductor worktree runs it concurrently. Build first, then `dotnet test --no-build -p:UseSharedCompilation=false`.

---

### Task 1: `LabelTextNormalizer`

The load-bearing pure function. Both the offline extractor and the runtime OCR path call it, so it is one implementation with its own tests rather than parallel logic that drifts.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/LabelTextNormalizer.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/LabelIdentification/LabelTextNormalizerTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `public static class LabelTextNormalizer { public static string Normalize(string? rawText); }` — returns `string.Empty` for null/blank input.

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.LabelIdentification.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.LabelIdentification;

public class LabelTextNormalizerTests
{
    [Fact]
    public void Strips_everything_up_to_and_including_the_ingredients_marker()
    {
        // The artwork carries a Czech job-name line above the sticker die-cut that is
        // never printed on the physical label. Left in, it pollutes the index with
        // tokens OCR can never match — and carries the size, breaking family identity.
        var raw = "Anela_Něžná paní Ovesná_15\n\n   Ingredients:\n Avena Sativa Kernel Extract";

        var result = LabelTextNormalizer.Normalize(raw);

        result.Should().Be("avena sativa kernel extract");
    }

    [Fact]
    public void Joins_hyphenation_across_line_breaks()
    {
        var result = LabelTextNormalizer.Normalize("Ingredients: Toco-\npherol");

        result.Should().Be("tocopherol");
    }

    [Fact]
    public void Collapses_whitespace_and_lowercases()
    {
        var result = LabelTextNormalizer.Normalize("Ingredients:   Rosa   CANINA\n\n Seed  Extract ");

        result.Should().Be("rosa canina seed extract");
    }

    [Fact]
    public void Converts_en_dash_and_slash_to_separators_preserving_ingredient_commas()
    {
        var result = LabelTextNormalizer.Normalize(
            "Ingredients: Cannabidiol – Derived From Extract, Caprylic/Capric Triglyceride");

        result.Should().Be("cannabidiol derived from extract, caprylic capric triglyceride");
    }

    [Fact]
    public void Strips_diacritics_and_form_feed_control_characters()
    {
        var result = LabelTextNormalizer.Normalize("Ingredients: Růže Oil");

        result.Should().Be("r e oil");
    }

    [Fact]
    public void Is_case_insensitive_about_the_ingredients_marker()
    {
        var result = LabelTextNormalizer.Normalize("INGREDIENTS: Tocopherol");

        result.Should().Be("tocopherol");
    }

    [Fact]
    public void Leaves_text_untouched_when_no_ingredients_marker_is_present()
    {
        var result = LabelTextNormalizer.Normalize("Tocopherol, Limonene");

        result.Should().Be("tocopherol, limonene");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Returns_empty_for_blank_input(string? raw)
    {
        LabelTextNormalizer.Normalize(raw).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~LabelTextNormalizerTests"
```

Expected: FAIL — `LabelTextNormalizer` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Text.RegularExpressions;

namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

/// <summary>
/// Reduces label text to a canonical form for matching. Called by both the offline
/// reference extractor and the runtime OCR path — one implementation so the two ends
/// cannot drift apart.
/// </summary>
public static class LabelTextNormalizer
{
    // The artwork PDFs carry a Czech job-name line above the sticker die-cut
    // (e.g. "Anela_Malá čarodějka_15ml_k") that is never printed on the physical
    // sticker. It also carries the size, which would break family identity.
    // Verified: all 37 reference PDFs contain exactly one "Ingredients" marker.
    private static readonly Regex IngredientsPrefix =
        new(@"^.*?ingredients\s*:", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HyphenLineBreak = new(@"-\s*\n", RegexOptions.Compiled);
    private static readonly Regex NonCanonical = new(@"[^a-z0-9, ]", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SpaceBeforeComma = new(@"\s+,", RegexOptions.Compiled);

    public static string Normalize(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        // Join hyphenation BEFORE collapsing whitespace, or the newline is already gone.
        var text = HyphenLineBreak.Replace(rawText, string.Empty);
        text = IngredientsPrefix.Replace(text, string.Empty);
        text = text.ToLowerInvariant();
        text = NonCanonical.Replace(text, " ");
        text = Whitespace.Replace(text, " ");
        text = SpaceBeforeComma.Replace(text, ",");

        return text.Trim();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~LabelTextNormalizerTests"
```

Expected: PASS (10 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/LabelTextNormalizer.cs \
        backend/test/Anela.Heblo.Tests/Features/LabelIdentification/LabelTextNormalizerTests.cs
git commit -m "feat: add label text normalizer"
```

---

### Task 2: Offline reference extractor + generated `label-references.json`

A one-time console tool. Committed so regeneration is reproducible when artwork changes.

**Files:**
- Create: `backend/tools/Anela.Heblo.LabelReferenceExtractor/Anela.Heblo.LabelReferenceExtractor.csproj`
- Create: `backend/tools/Anela.Heblo.LabelReferenceExtractor/Program.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/Data/label-references.json` (generated output)
- Modify: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` (embed the resource)

**Interfaces:**
- Consumes: `LabelTextNormalizer.Normalize` (Task 1)
- Produces: `label-references.json` — a JSON array of `{ "family": string, "codes": string[], "normalized": string }`, 25 entries, `codes` sorted ascending, entries sorted by `family`.

- [ ] **Step 1: Create the tool project**

`backend/tools/Anela.Heblo.LabelReferenceExtractor/Anela.Heblo.LabelReferenceExtractor.csproj` — mirrors the existing `Anela.Heblo.AccessMatrixGen` tool layout:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="PdfPig" Version="0.1.9" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Anela.Heblo.Application/Anela.Heblo.Application.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the extractor**

`backend/tools/Anela.Heblo.LabelReferenceExtractor/Program.cs`:

```csharp
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
```

- [ ] **Step 3: Add the tool to the solution and generate the data**

```bash
dotnet sln Anela.Heblo.sln add backend/tools/Anela.Heblo.LabelReferenceExtractor/Anela.Heblo.LabelReferenceExtractor.csproj
dotnet run --project backend/tools/Anela.Heblo.LabelReferenceExtractor -- \
  data/labels \
  backend/src/Anela.Heblo.Application/Features/LabelIdentification/Data/label-references.json
```

Expected stdout: `37 labels -> 25 families -> ...`, exit code 0, no `FAILED` lines.

- [ ] **Step 4: Verify the generated data matches the spec's measured shape**

```bash
python3 -c "
import json
e = json.load(open('backend/src/Anela.Heblo.Application/Features/LabelIdentification/Data/label-references.json'))
assert len(e) == 25, f'expected 25 families, got {len(e)}'
assert sum(len(x['codes']) for x in e) == 37
assert len([x for x in e if len(x['codes']) > 1]) == 12
assert len([x for x in e if len(x['codes']) == 1]) == 13
assert all(c[:6] == x['family'] for x in e for c in x['codes'])
assert all(x['normalized'].strip() for x in e)
assert not any('anela' in x['normalized'] for x in e), 'job-name line leaked into the index'
print('OK', len(e), 'families')
"
```

Expected: `OK 25 families`.

- [ ] **Step 5: Embed the resource**

In `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`, add inside a new `<ItemGroup>`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Features\LabelIdentification\Data\label-references.json" />
  </ItemGroup>
```

- [ ] **Step 6: Commit**

```bash
dotnet build Anela.Heblo.sln
git add backend/tools/Anela.Heblo.LabelReferenceExtractor \
        backend/src/Anela.Heblo.Application/Features/LabelIdentification/Data/label-references.json \
        backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj \
        Anela.Heblo.sln
git commit -m "feat: add label reference extractor and generated reference data"
```

---

### Task 3: `LabelReferenceIndex`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/LabelReferenceEntry.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/LabelReferenceIndex.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/LabelIdentification/LabelReferenceIndexTests.cs`

**Interfaces:**
- Consumes: the embedded `label-references.json` (Task 2)
- Produces:
  - `public sealed class LabelReferenceEntry { public string Family { get; init; } public IReadOnlyList<string> Codes { get; init; } public string Normalized { get; init; } public IReadOnlySet<string> Tokens { get; init; } }`
  - `public interface ILabelReferenceIndex { IReadOnlyList<LabelReferenceEntry> Entries { get; } }`
  - `public sealed class LabelReferenceIndex : ILabelReferenceIndex` — parameterless constructor, loads the embedded resource once.

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.LabelIdentification.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.LabelIdentification;

public class LabelReferenceIndexTests
{
    private readonly LabelReferenceIndex _index = new();

    [Fact]
    public void Loads_twenty_five_families_covering_thirty_seven_product_codes()
    {
        _index.Entries.Should().HaveCount(25);
        _index.Entries.Sum(e => e.Codes.Count).Should().Be(37);
    }

    [Fact]
    public void Splits_into_twelve_two_size_families_and_thirteen_single_size_families()
    {
        _index.Entries.Count(e => e.Codes.Count > 1).Should().Be(12);
        _index.Entries.Count(e => e.Codes.Count == 1).Should().Be(13);
    }

    [Fact]
    public void Every_code_maps_back_to_its_family_prefix()
    {
        foreach (var entry in _index.Entries)
        {
            entry.Codes.Should().OnlyContain(code => code.StartsWith(entry.Family));
            entry.Family.Should().HaveLength(6);
        }
    }

    [Fact]
    public void Every_entry_has_normalized_text_and_precomputed_tokens()
    {
        _index.Entries.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.Normalized));
        _index.Entries.Should().OnlyContain(e => e.Tokens.Count > 0);
    }

    [Fact]
    public void Index_excludes_the_artwork_job_name_line()
    {
        // "Anela_Malá čarodějka_15ml_k" and friends are never printed on the sticker.
        _index.Entries.Should().OnlyContain(e => !e.Normalized.Contains("anela"));
    }

    [Fact]
    public void Known_two_size_family_exposes_both_variants()
    {
        var kre005 = _index.Entries.Single(e => e.Family == "KRE005");

        kre005.Codes.Should().BeEquivalentTo(new[] { "KRE005015", "KRE005030" });
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~LabelReferenceIndexTests"
```

Expected: FAIL — `LabelReferenceIndex` does not exist.

- [ ] **Step 3: Write the implementation**

`LabelReferenceEntry.cs`:

```csharp
namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

public sealed class LabelReferenceEntry
{
    public required string Family { get; init; }
    public required IReadOnlyList<string> Codes { get; init; }
    public required string Normalized { get; init; }

    /// <summary>Comma-split ingredient set, precomputed for Jaccard overlap.</summary>
    public required IReadOnlySet<string> Tokens { get; init; }

    public static IReadOnlySet<string> Tokenize(string normalized) =>
        normalized
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
}
```

`LabelReferenceIndex.cs`:

```csharp
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

public interface ILabelReferenceIndex
{
    IReadOnlyList<LabelReferenceEntry> Entries { get; }
}

/// <summary>
/// Immutable in-memory index of label reference text, loaded once from an embedded
/// resource. Nothing parses a PDF at request time — the reference data is the
/// extracted, normalized INCI text (~27 KB for the whole catalogue).
/// </summary>
public sealed class LabelReferenceIndex : ILabelReferenceIndex
{
    private const string ResourceName =
        "Anela.Heblo.Application.Features.LabelIdentification.Data.label-references.json";

    public IReadOnlyList<LabelReferenceEntry> Entries { get; }

    public LabelReferenceIndex()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. Regenerate it with " +
                "Anela.Heblo.LabelReferenceExtractor and ensure the csproj embeds it.");

        var raw = JsonSerializer.Deserialize<List<RawEntry>>(stream)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is not valid JSON.");

        Entries = raw
            .Select(e => new LabelReferenceEntry
            {
                Family = e.Family,
                Codes = e.Codes,
                Normalized = e.Normalized,
                Tokens = LabelReferenceEntry.Tokenize(e.Normalized),
            })
            .ToList();
    }

    private sealed class RawEntry
    {
        [JsonPropertyName("family")]
        public string Family { get; set; } = string.Empty;

        [JsonPropertyName("codes")]
        public List<string> Codes { get; set; } = new();

        [JsonPropertyName("normalized")]
        public string Normalized { get; set; } = string.Empty;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~LabelReferenceIndexTests"
```

Expected: PASS (6 tests). If the resource is not found, verify the `<EmbeddedResource>` path from Task 2 Step 5 and that the logical name matches `ResourceName`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/LabelReferenceEntry.cs \
        backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/LabelReferenceIndex.cs \
        backend/test/Anela.Heblo.Tests/Features/LabelIdentification/LabelReferenceIndexTests.cs
git commit -m "feat: add embedded label reference index"
```

---

### Task 4: `LabelMatcher`

The other pure module. Tests seed it with the **real** index so they encode actual catalogue behaviour.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/LabelIdentificationOptions.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/LabelMatchDecision.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/LabelMatch.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/LabelMatcher.cs`
- Modify: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` (add FuzzySharp)
- Test: `backend/test/Anela.Heblo.Tests/Features/LabelIdentification/LabelMatcherTests.cs`

**Interfaces:**
- Consumes: `ILabelReferenceIndex` (Task 3), `LabelTextNormalizer` (Task 1)
- Produces:
  - `public enum LabelMatchDecision { Auto, Choose, Low }`
  - `public sealed class LabelMatch { public string Family { get; init; } public IReadOnlyList<string> Codes { get; init; } public double Score { get; init; } }`
  - `public sealed class LabelMatchResult { public LabelMatchDecision Decision { get; init; } public IReadOnlyList<LabelMatch> Candidates { get; init; } }`
  - `public interface ILabelMatcher { LabelMatchResult Match(string normalizedText); }`
  - `public sealed class LabelMatcher : ILabelMatcher` — ctor `(ILabelReferenceIndex index, IOptions<LabelIdentificationOptions> options)`

- [ ] **Step 1: Add the FuzzySharp package**

In `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`, add to the existing `<ItemGroup>` of `PackageReference`s:

```xml
    <PackageReference Include="FuzzySharp" Version="2.0.2" />
```

- [ ] **Step 2: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.LabelIdentification;
using Anela.Heblo.Application.Features.LabelIdentification.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.LabelIdentification;

public class LabelMatcherTests
{
    private readonly LabelReferenceIndex _index = new();
    private readonly LabelMatcher _matcher;

    public LabelMatcherTests()
    {
        _matcher = new LabelMatcher(_index, Options.Create(new LabelIdentificationOptions()));
    }

    private string TextFor(string family) =>
        _index.Entries.Single(e => e.Family == family).Normalized;

    [Fact]
    public void Exact_reference_text_auto_confirms_the_right_family()
    {
        var result = _matcher.Match(TextFor("KRE005"));

        result.Decision.Should().Be(LabelMatchDecision.Auto);
        result.Candidates[0].Family.Should().Be("KRE005");
        result.Candidates[0].Score.Should().BeApproximately(100d, 0.01);
    }

    [Fact]
    public void Two_size_family_returns_both_variant_codes()
    {
        var result = _matcher.Match(TextFor("KRE005"));

        result.Candidates[0].Codes.Should().BeEquivalentTo(new[] { "KRE005015", "KRE005030" });
    }

    [Fact]
    public void Single_size_family_returns_one_code()
    {
        var result = _matcher.Match(TextFor("PEE002"));

        result.Candidates[0].Family.Should().Be("PEE002");
        result.Candidates[0].Codes.Should().ContainSingle();
    }

    [Fact]
    public void The_closest_confusable_pair_still_clears_the_auto_margin()
    {
        // MAS007 vs KRE005 score 87.7 against each other — the tightest pair in the
        // corpus. A perfect KRE005 match must still win by more than the margin.
        var result = _matcher.Match(TextFor("KRE005"));

        result.Candidates[0].Family.Should().Be("KRE005");
        result.Candidates[1].Family.Should().Be("MAS007");
        (result.Candidates[0].Score - result.Candidates[1].Score).Should().BeGreaterThan(5d);
        result.Decision.Should().Be(LabelMatchDecision.Auto);
    }

    [Fact]
    public void Survives_appended_ghost_text_from_neighbouring_stickers()
    {
        var text = TextFor("MAS001");
        var withGhost = text + ", " + string.Join(", ", text.Split(',').Take(5));

        var result = _matcher.Match(withGhost);

        result.Candidates[0].Family.Should().Be("MAS001");
        result.Decision.Should().Be(LabelMatchDecision.Auto);
    }

    [Fact]
    public void Survives_reordered_ingredients()
    {
        var parts = TextFor("MAS001").Split(',', StringSplitOptions.TrimEntries).ToList();
        parts.Reverse();

        var result = _matcher.Match(string.Join(", ", parts));

        result.Candidates[0].Family.Should().Be("MAS001");
    }

    [Fact]
    public void Survives_dropped_characters_from_imperfect_ocr()
    {
        var text = TextFor("MAS001");
        var mangled = new string(text.Where((_, i) => i % 17 != 0).ToArray());

        var result = _matcher.Match(mangled);

        result.Candidates[0].Family.Should().Be("MAS001");
    }

    [Fact]
    public void Garbage_input_is_low_confidence_and_never_a_confident_code()
    {
        var result = _matcher.Match("qqq www zzz nothing like an ingredient list at all");

        result.Decision.Should().Be(LabelMatchDecision.Low);
    }

    [Fact]
    public void Returns_at_most_three_candidates_ranked_descending()
    {
        var result = _matcher.Match(TextFor("MAS001"));

        result.Candidates.Should().HaveCount(3);
        result.Candidates.Should().BeInDescendingOrder(c => c.Score);
    }

    [Fact]
    public void Blank_input_is_low_confidence_with_no_candidates()
    {
        var result = _matcher.Match("   ");

        result.Decision.Should().Be(LabelMatchDecision.Low);
        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public void A_narrow_lead_falls_back_to_choose_rather_than_auto_confirming()
    {
        var options = Options.Create(new LabelIdentificationOptions { AutoConfirmMargin = 99 });
        var matcher = new LabelMatcher(_index, options);

        var result = matcher.Match(TextFor("KRE005"));

        result.Decision.Should().Be(LabelMatchDecision.Choose);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~LabelMatcherTests"
```

Expected: FAIL — `LabelMatcher` does not exist.

- [ ] **Step 4: Write the implementation**

`LabelIdentificationOptions.cs`:

```csharp
namespace Anela.Heblo.Application.Features.LabelIdentification;

public class LabelIdentificationOptions
{
    public const string SectionKey = "LabelIdentification";

    /// <summary>Blended score at or above which a match may auto-confirm.</summary>
    public double AutoConfirmScore { get; set; } = 90;

    /// <summary>Required lead over the runner-up for auto-confirmation.</summary>
    public double AutoConfirmMargin { get; set; } = 5;

    /// <summary>Below this blended score the result is reported as unreadable.</summary>
    public double LowConfidenceFloor { get; set; } = 60;

    /// <summary>Longest edge, in px, the photo is downscaled to before the vision call.</summary>
    public int MaxImageEdge { get; set; } = 2048;

    /// <summary>Upload size cap in bytes.</summary>
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;
}
```

`LabelMatchDecision.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LabelMatchDecision
{
    Auto,
    Choose,
    Low,
}
```

`LabelMatch.cs`:

```csharp
namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

public sealed class LabelMatch
{
    public required string Family { get; init; }
    public required IReadOnlyList<string> Codes { get; init; }
    public required double Score { get; init; }
}

public sealed class LabelMatchResult
{
    public required LabelMatchDecision Decision { get; init; }
    public required IReadOnlyList<LabelMatch> Candidates { get; init; }
}
```

`LabelMatcher.cs`:

```csharp
using FuzzySharp;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

public interface ILabelMatcher
{
    LabelMatchResult Match(string normalizedText);
}

/// <summary>
/// Ranks label reference families against normalized OCR text.
///
/// Matching is done at FAMILY level, not product-code level. Eleven of the 37 reference
/// labels are byte-identical to their size sibling because the 015/030 suffix is sticker
/// size, not composition — so a code-level index always ties and can never auto-confirm.
/// Grouping by family removes every tie; the operator picks the size afterwards.
/// </summary>
public sealed class LabelMatcher : ILabelMatcher
{
    private const double TokenSetWeight = 0.7;
    private const double JaccardWeight = 0.3;
    private const int MaxCandidates = 3;

    private readonly ILabelReferenceIndex _index;
    private readonly LabelIdentificationOptions _options;

    public LabelMatcher(ILabelReferenceIndex index, IOptions<LabelIdentificationOptions> options)
    {
        _index = index;
        _options = options.Value;
    }

    public LabelMatchResult Match(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return new LabelMatchResult
            {
                Decision = LabelMatchDecision.Low,
                Candidates = Array.Empty<LabelMatch>(),
            };
        }

        var queryTokens = LabelReferenceEntry.Tokenize(normalizedText);

        var ranked = _index.Entries
            .Select(entry => new LabelMatch
            {
                Family = entry.Family,
                Codes = entry.Codes,
                Score = Score(normalizedText, queryTokens, entry),
            })
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Family, StringComparer.Ordinal)
            .Take(MaxCandidates)
            .ToList();

        return new LabelMatchResult
        {
            Decision = Decide(ranked),
            Candidates = ranked,
        };
    }

    private static double Score(string query, IReadOnlySet<string> queryTokens, LabelReferenceEntry entry)
    {
        // token_set_ratio is robust against duplicated ghost text and reordering —
        // precisely the failure mode of photographing a roll of stickers.
        double tokenSet = Fuzz.TokenSetRatio(query, entry.Normalized);

        // Jaccard over comma-split ingredients sees boundaries word-level matching ignores.
        var union = queryTokens.Count + entry.Tokens.Count - queryTokens.Count(entry.Tokens.Contains);
        var intersection = queryTokens.Count(entry.Tokens.Contains);
        var jaccard = union == 0 ? 0d : (double)intersection / union * 100d;

        return TokenSetWeight * tokenSet + JaccardWeight * jaccard;
    }

    private LabelMatchDecision Decide(IReadOnlyList<LabelMatch> ranked)
    {
        if (ranked.Count == 0)
        {
            return LabelMatchDecision.Low;
        }

        var best = ranked[0].Score;
        var runnerUp = ranked.Count > 1 ? ranked[1].Score : 0d;

        if (best >= _options.AutoConfirmScore && best - runnerUp >= _options.AutoConfirmMargin)
        {
            return LabelMatchDecision.Auto;
        }

        return best >= _options.LowConfidenceFloor
            ? LabelMatchDecision.Choose
            : LabelMatchDecision.Low;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~LabelMatcherTests"
```

Expected: PASS (11 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/LabelIdentification \
        backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj \
        backend/test/Anela.Heblo.Tests/Features/LabelIdentification/LabelMatcherTests.cs
git commit -m "feat: add family-level label matcher"
```

---

### Task 5: Error codes

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`

**Interfaces:**
- Produces: `ErrorCodes.LabelPhotoMissingOrInvalid` (3301), `ErrorCodes.LabelPhotoUndecodable` (3302), `ErrorCodes.LabelTextUnreadable` (3303).

- [ ] **Step 1: Add the module block**

`32XX` (Authorization) is currently the highest module block; `33XX` is free. Insert immediately **before** the `// External Service errors (90XX)` comment:

```csharp
    // Label identification module errors (33XX)
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    LabelPhotoMissingOrInvalid = 3301,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    LabelPhotoUndecodable = 3302,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    LabelTextUnreadable = 3303,

```

Each situation gets a distinct code because the frontend error map keys on it — reusing the generic `ValidationError` for three different failures would make them indistinguishable on the phone.

- [ ] **Step 2: Verify the build and that no existing code collides**

```bash
dotnet build Anela.Heblo.sln
grep -n "330[123]" backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
```

Expected: build succeeds; exactly the three new lines are listed.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat: add label identification error codes"
```

---

### Task 6: Vision support in `AnthropicChatClient`

`GetResponseAsync` currently flattens every message with `content = m.Text`, so it is text-only. This adds image content blocks. The `IChatClient` surface, the Polly pipeline, and all existing callers are unchanged — Photobank, Article, and RAG gain vision as a side effect.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicChatClient.cs`
- Test: `backend/test/Anela.Heblo.Tests/Adapters/Anthropic/AnthropicChatClientVisionTests.cs`

**Interfaces:**
- Produces: no signature change. `ChatMessage`s whose `Contents` include a `DataContent` with an `image/*` media type now serialize as Anthropic image blocks.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.Anthropic;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Anthropic;

public class AnthropicChatClientVisionTests
{
    private string? _capturedBody;

    private AnthropicChatClient CreateClient()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage req, CancellationToken _) =>
            {
                _capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"content":[{"type":"text","text":"Tocopherol, Limonene"}]}""",
                        Encoding.UTF8, "application/json"),
                };
            });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Anthropic"))
            .Returns(new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.anthropic.com") });

        return new AnthropicChatClient(
            Options.Create(new AnthropicOptions { ApiKey = "test-key" }),
            factory.Object,
            NullLogger<AnthropicChatClient>.Instance);
    }

    private static ChatMessage PhotoMessage() =>
        new(ChatRole.User, new List<AIContent>
        {
            new DataContent(new byte[] { 1, 2, 3, 4 }, "image/jpeg"),
            new TextContent("Read the ingredients."),
        });

    [Fact]
    public async Task Serializes_image_content_as_an_anthropic_image_block()
    {
        var client = CreateClient();

        await client.GetResponseAsync(new[] { PhotoMessage() });

        using var doc = JsonDocument.Parse(_capturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        content.ValueKind.Should().Be(JsonValueKind.Array);
        var imageBlock = content.EnumerateArray().Single(b => b.GetProperty("type").GetString() == "image");
        var source = imageBlock.GetProperty("source");
        source.GetProperty("type").GetString().Should().Be("base64");
        source.GetProperty("media_type").GetString().Should().Be("image/jpeg");
        source.GetProperty("data").GetString().Should().Be(Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public async Task Includes_the_accompanying_text_block_alongside_the_image()
    {
        var client = CreateClient();

        await client.GetResponseAsync(new[] { PhotoMessage() });

        using var doc = JsonDocument.Parse(_capturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");
        var textBlock = content.EnumerateArray().Single(b => b.GetProperty("type").GetString() == "text");

        textBlock.GetProperty("text").GetString().Should().Be("Read the ingredients.");
    }

    [Fact]
    public async Task Text_only_messages_still_serialize_content_as_a_plain_string()
    {
        var client = CreateClient();

        await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hello") });

        using var doc = JsonDocument.Parse(_capturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        content.ValueKind.Should().Be(JsonValueKind.String);
        content.GetString().Should().Be("Hello");
    }

    [Fact]
    public async Task Returns_the_assistant_text_from_the_response()
    {
        var client = CreateClient();

        var response = await client.GetResponseAsync(new[] { PhotoMessage() });

        response.Messages[0].Text.Should().Be("Tocopherol, Limonene");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~AnthropicChatClientVisionTests"
```

Expected: FAIL — the first two tests fail because `content` is a plain string.

- [ ] **Step 3: Replace the user-message projection**

In `AnthropicChatClient.GetResponseAsync`, replace:

```csharp
        var userMessages = messageList
            .Where(m => m.Role == ChatRole.User)
            .Select(m => new { role = "user", content = m.Text })
            .ToArray();
```

with:

```csharp
        var userMessages = messageList
            .Where(m => m.Role == ChatRole.User)
            .Select(BuildUserMessage)
            .ToArray();
```

Then add these private members to the class:

```csharp
    /// <summary>
    /// Messages carrying image data serialize as an Anthropic content-block array;
    /// text-only messages keep the plain-string form every existing caller relies on.
    /// </summary>
    private static object BuildUserMessage(ChatMessage message)
    {
        var imageContents = message.Contents
            .OfType<DataContent>()
            .Where(c => c.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        if (imageContents.Count == 0)
        {
            return new { role = "user", content = message.Text };
        }

        var blocks = new List<object>();
        foreach (var image in imageContents)
        {
            blocks.Add(new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = image.MediaType,
                    data = Convert.ToBase64String(image.Data.ToArray()),
                },
            });
        }

        var text = string.Concat(message.Contents.OfType<TextContent>().Select(c => c.Text));
        if (!string.IsNullOrEmpty(text))
        {
            blocks.Add(new { type = "text", text });
        }

        return new { role = "user", content = blocks };
    }
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~AnthropicChatClient"
```

Expected: PASS — the four new tests plus every pre-existing `AnthropicChatClient` test (regression check that text-only callers are unaffected).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicChatClient.cs \
        backend/test/Anela.Heblo.Tests/Adapters/Anthropic/AnthropicChatClientVisionTests.cs
git commit -m "feat: support image content blocks in AnthropicChatClient"
```

---

### Task 7: `ILabelOcrService`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/ILabelOcrService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/AnthropicLabelOcrService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/LabelIdentification/AnthropicLabelOcrServiceTests.cs`

**Interfaces:**
- Consumes: `IChatClient` (Task 6), `LabelIdentificationOptions` (Task 4)
- Produces:
  - `public sealed class LabelOcrException : Exception`
  - `public interface ILabelOcrService { Task<string> ReadIngredientsAsync(Stream photo, CancellationToken ct); }` — returns the raw ingredient line; throws `LabelOcrException` when the image cannot be decoded.

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.LabelIdentification;
using Anela.Heblo.Application.Features.LabelIdentification.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SkiaSharp;
using Xunit;

namespace Anela.Heblo.Tests.Features.LabelIdentification;

public class AnthropicLabelOcrServiceTests
{
    private readonly Mock<IChatClient> _chatClient = new();

    private AnthropicLabelOcrService CreateService() => new(
        _chatClient.Object,
        Options.Create(new LabelIdentificationOptions()),
        NullLogger<AnthropicLabelOcrService>.Instance);

    private static Stream JpegPhoto(int width = 4000, int height = 3000)
    {
        using var bitmap = new SKBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return new MemoryStream(data.ToArray());
    }

    private void SetupResponse(string text) =>
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]));

    [Fact]
    public async Task Returns_the_ingredient_line_from_the_model()
    {
        SetupResponse("Tocopherol, Limonene, Linalool");

        var result = await CreateService().ReadIngredientsAsync(JpegPhoto(), CancellationToken.None);

        result.Should().Be("Tocopherol, Limonene, Linalool");
    }

    [Fact]
    public async Task Sends_the_photo_as_image_content()
    {
        SetupResponse("Tocopherol");
        IEnumerable<ChatMessage>? captured = null;
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => captured = m.ToList())
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Tocopherol")]));

        await CreateService().ReadIngredientsAsync(JpegPhoto(), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.SelectMany(m => m.Contents).OfType<DataContent>()
            .Should().ContainSingle(c => c.MediaType == "image/jpeg");
    }

    [Fact]
    public async Task Downscales_the_photo_to_the_configured_longest_edge()
    {
        SetupResponse("Tocopherol");
        IEnumerable<ChatMessage>? captured = null;
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => captured = m.ToList())
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Tocopherol")]));

        await CreateService().ReadIngredientsAsync(JpegPhoto(4000, 3000), CancellationToken.None);

        var sent = captured!.SelectMany(m => m.Contents).OfType<DataContent>().Single();
        using var decoded = SKBitmap.Decode(sent.Data.ToArray());
        Math.Max(decoded.Width, decoded.Height).Should().Be(2048);
    }

    [Fact]
    public async Task Throws_LabelOcrException_when_the_image_cannot_be_decoded()
    {
        var service = CreateService();
        using var garbage = new MemoryStream(new byte[] { 0, 1, 2, 3, 4, 5 });

        var act = () => service.ReadIngredientsAsync(garbage, CancellationToken.None);

        await act.Should().ThrowAsync<LabelOcrException>();
    }

    [Fact]
    public async Task Returns_empty_when_the_model_returns_nothing()
    {
        SetupResponse("   ");

        var result = await CreateService().ReadIngredientsAsync(JpegPhoto(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Does_not_swallow_transport_failures()
    {
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("upstream down"));

        var act = () => CreateService().ReadIngredientsAsync(JpegPhoto(), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~AnthropicLabelOcrServiceTests"
```

Expected: FAIL — `AnthropicLabelOcrService` does not exist.

- [ ] **Step 3: Add SkiaSharp to the Application project**

`SkiaSharp` is referenced elsewhere in the backend but may not be on `Anela.Heblo.Application`. Add to `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` if absent:

```xml
    <PackageReference Include="SkiaSharp" Version="2.88.3" />
```

- [ ] **Step 4: Write the implementation**

`ILabelOcrService.cs`:

```csharp
namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

/// <summary>Raised when the uploaded photo cannot be decoded as an image.</summary>
public sealed class LabelOcrException : Exception
{
    public LabelOcrException(string message) : base(message) { }
}

public interface ILabelOcrService
{
    /// <summary>
    /// Transcribes the ingredient list from a label photo. Returns an empty string when
    /// the model finds nothing readable. Throws <see cref="LabelOcrException"/> when the
    /// photo cannot be decoded; transport failures propagate as-is.
    /// </summary>
    Task<string> ReadIngredientsAsync(Stream photo, CancellationToken cancellationToken);
}
```

`AnthropicLabelOcrService.cs`:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

public sealed class AnthropicLabelOcrService : ILabelOcrService
{
    private const int JpegQuality = 90;

    // Constrained to one job the model is good at: read text. Labels on a roll are all
    // the same product, so rotation, blur, and ghost text bleeding in from neighbouring
    // stickers are expected and harmless.
    private const string Prompt =
        "This is a photo of a cosmetic product label on a roll of stickers. " +
        "Return the INCI ingredient list of ONE label as a single comma-separated line. " +
        "All stickers on the roll are the same product. Ignore rotation, blur, and any " +
        "partial text bleeding in from neighbouring stickers. " +
        "Return only the ingredient list — no preamble, no explanation, no 'Ingredients:' prefix. " +
        "If no ingredients are legible, return nothing at all.";

    private readonly IChatClient _chatClient;
    private readonly LabelIdentificationOptions _options;
    private readonly ILogger<AnthropicLabelOcrService> _logger;

    public AnthropicLabelOcrService(
        IChatClient chatClient,
        IOptions<LabelIdentificationOptions> options,
        ILogger<AnthropicLabelOcrService> logger)
    {
        _chatClient = chatClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> ReadIngredientsAsync(Stream photo, CancellationToken cancellationToken)
    {
        var jpeg = Downscale(photo);

        var message = new ChatMessage(ChatRole.User, new List<AIContent>
        {
            new DataContent(jpeg, "image/jpeg"),
            new TextContent(Prompt),
        });

        var response = await _chatClient.GetResponseAsync(new[] { message }, cancellationToken: cancellationToken);
        var text = response.Messages.FirstOrDefault()?.Text ?? string.Empty;

        _logger.LogDebug("Label OCR returned {Length} characters", text.Length);

        return text.Trim();
    }

    private byte[] Downscale(Stream photo)
    {
        using var original = SKBitmap.Decode(photo)
            ?? throw new LabelOcrException("Photo could not be decoded as an image.");

        var longestEdge = Math.Max(original.Width, original.Height);
        var bitmap = original;
        SKBitmap? resized = null;

        if (longestEdge > _options.MaxImageEdge)
        {
            var scale = (double)_options.MaxImageEdge / longestEdge;
            var width = (int)Math.Round(original.Width * scale);
            var height = (int)Math.Round(original.Height * scale);

            resized = original.Resize(new SKImageInfo(width, height), SKFilterQuality.Medium)
                ?? throw new LabelOcrException("Photo could not be resized.");
            bitmap = resized;
        }

        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            return data.ToArray();
        }
        finally
        {
            resized?.Dispose();
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~AnthropicLabelOcrServiceTests"
```

Expected: PASS (6 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/ILabelOcrService.cs \
        backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/AnthropicLabelOcrService.cs \
        backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj \
        backend/test/Anela.Heblo.Tests/Features/LabelIdentification/AnthropicLabelOcrServiceTests.cs
git commit -m "feat: add Anthropic label OCR service"
```

---

### Task 8: Contracts, handler, and module registration

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/UseCases/IdentifyLabel/IdentifyLabelRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/UseCases/IdentifyLabel/IdentifyLabelRequestValidator.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/UseCases/IdentifyLabel/LabelVariantDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/UseCases/IdentifyLabel/LabelCandidateDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/UseCases/IdentifyLabel/IdentifyLabelResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/UseCases/IdentifyLabel/IdentifyLabelHandler.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/LabelIdentification/LabelIdentificationModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Program.cs` (call `AddLabelIdentificationModule`)
- Test: `backend/test/Anela.Heblo.Tests/Features/LabelIdentification/IdentifyLabelHandlerTests.cs`

**Interfaces:**
- Consumes: `ILabelOcrService` (Task 7), `ILabelMatcher` (Task 4), `LabelTextNormalizer` (Task 1), `ICatalogRepository` (existing), `ErrorCodes` (Task 5)
- Produces: `IdentifyLabelRequest : IRequest<IdentifyLabelResponse>`, `IdentifyLabelResponse : BaseResponse`, and `IServiceCollection.AddLabelIdentificationModule()`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.LabelIdentification;
using Anela.Heblo.Application.Features.LabelIdentification.Services;
using Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.LabelIdentification;

public class IdentifyLabelHandlerTests
{
    private readonly Mock<ILabelOcrService> _ocr = new();
    private readonly Mock<ICatalogRepository> _catalog = new();
    private readonly LabelReferenceIndex _index = new();

    private IdentifyLabelHandler CreateHandler()
    {
        var matcher = new LabelMatcher(_index, Options.Create(new LabelIdentificationOptions()));
        return new IdentifyLabelHandler(
            _ocr.Object, matcher, _catalog.Object, NullLogger<IdentifyLabelHandler>.Instance);
    }

    private string TextFor(string family) => _index.Entries.Single(e => e.Family == family).Normalized;

    private static IdentifyLabelRequest RequestWithPhoto() => new()
    {
        PhotoStream = new MemoryStream(new byte[] { 1, 2, 3 }),
        ContentType = "image/jpeg",
        SizeBytes = 3,
    };

    private void SetupCatalogName(string code, string name) =>
        _catalog.Setup(c => c.GetByIdAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate { ProductCode = code, ProductName = name });

    [Fact]
    public async Task Auto_decision_returns_the_matched_family_with_resolved_product_names()
    {
        _ocr.Setup(o => o.ReadIngredientsAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextFor("KRE005"));
        SetupCatalogName("KRE005015", "Masážní olej 15 ml");
        SetupCatalogName("KRE005030", "Masážní olej 30 ml");

        var response = await CreateHandler().Handle(RequestWithPhoto(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Decision.Should().Be(LabelMatchDecision.Auto);
        response.Candidates[0].Family.Should().Be("KRE005");
        response.Candidates[0].Variants.Should().HaveCount(2);
        response.Candidates[0].Variants.Select(v => v.ProductName)
            .Should().BeEquivalentTo(new[] { "Masážní olej 15 ml", "Masážní olej 30 ml" });
    }

    [Fact]
    public async Task Missing_catalog_entry_yields_an_empty_name_but_still_returns_the_code()
    {
        _ocr.Setup(o => o.ReadIngredientsAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextFor("KRE005"));
        _catalog.Setup(c => c.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate?)null);

        var response = await CreateHandler().Handle(RequestWithPhoto(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Candidates[0].Variants.Should().OnlyContain(v => v.ProductName == string.Empty);
        response.Candidates[0].Variants.Should().OnlyContain(v => v.ProductCode.StartsWith("KRE005"));
    }

    [Fact]
    public async Task Returns_the_raw_transcribed_text_for_troubleshooting()
    {
        _ocr.Setup(o => o.ReadIngredientsAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Tocopherol, Limonene");
        _catalog.Setup(c => c.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate?)null);

        var response = await CreateHandler().Handle(RequestWithPhoto(), CancellationToken.None);

        response.RawText.Should().Be("Tocopherol, Limonene");
    }

    [Fact]
    public async Task Empty_transcription_fails_with_LabelTextUnreadable()
    {
        _ocr.Setup(o => o.ReadIngredientsAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var response = await CreateHandler().Handle(RequestWithPhoto(), CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.LabelTextUnreadable);
    }

    [Fact]
    public async Task Undecodable_photo_fails_with_LabelPhotoUndecodable()
    {
        _ocr.Setup(o => o.ReadIngredientsAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LabelOcrException("bad image"));

        var response = await CreateHandler().Handle(RequestWithPhoto(), CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.LabelPhotoUndecodable);
    }

    [Fact]
    public async Task Upstream_failure_fails_with_ExternalServiceError()
    {
        _ocr.Setup(o => o.ReadIngredientsAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("upstream down"));

        var response = await CreateHandler().Handle(RequestWithPhoto(), CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ExternalServiceError);
    }

    [Fact]
    public async Task Garbage_transcription_returns_Low_as_a_successful_response()
    {
        // Low is a real answer, not an error — the UI shows a retry prompt with candidates.
        _ocr.Setup(o => o.ReadIngredientsAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("qqq www zzz nothing like an ingredient list");
        _catalog.Setup(c => c.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate?)null);

        var response = await CreateHandler().Handle(RequestWithPhoto(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Decision.Should().Be(LabelMatchDecision.Low);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~IdentifyLabelHandlerTests"
```

Expected: FAIL — the use-case types do not exist.

- [ ] **Step 3: Write the contracts**

`IdentifyLabelRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;

public class IdentifyLabelRequest : IRequest<IdentifyLabelResponse>
{
    public Stream PhotoStream { get; set; } = Stream.Null;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
```

`LabelVariantDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;

public class LabelVariantDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
}
```

`LabelCandidateDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;

public class LabelCandidateDto
{
    public string Family { get; set; } = string.Empty;
    public double Score { get; set; }
    public List<LabelVariantDto> Variants { get; set; } = new();
}
```

`IdentifyLabelResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.LabelIdentification.Services;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;

public class IdentifyLabelResponse : BaseResponse
{
    public string RawText { get; set; } = string.Empty;
    public LabelMatchDecision Decision { get; set; }
    public List<LabelCandidateDto> Candidates { get; set; } = new();

    public IdentifyLabelResponse() { }

    public IdentifyLabelResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

`IdentifyLabelRequestValidator.cs`:

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;

public class IdentifyLabelRequestValidator : AbstractValidator<IdentifyLabelRequest>
{
    public IdentifyLabelRequestValidator()
    {
        RuleFor(x => x.SizeBytes).GreaterThan(0);
        RuleFor(x => x.ContentType)
            .Must(ct => ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Uploaded file must be an image.");
    }
}
```

- [ ] **Step 4: Write the handler**

`IdentifyLabelHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.LabelIdentification.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;

public class IdentifyLabelHandler : IRequestHandler<IdentifyLabelRequest, IdentifyLabelResponse>
{
    private readonly ILabelOcrService _ocrService;
    private readonly ILabelMatcher _matcher;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<IdentifyLabelHandler> _logger;

    public IdentifyLabelHandler(
        ILabelOcrService ocrService,
        ILabelMatcher matcher,
        ICatalogRepository catalogRepository,
        ILogger<IdentifyLabelHandler> logger)
    {
        _ocrService = ocrService;
        _matcher = matcher;
        _catalogRepository = catalogRepository;
        _logger = logger;
    }

    public async Task<IdentifyLabelResponse> Handle(
        IdentifyLabelRequest request,
        CancellationToken cancellationToken)
    {
        string rawText;
        try
        {
            rawText = await _ocrService.ReadIngredientsAsync(request.PhotoStream, cancellationToken);
        }
        catch (LabelOcrException ex)
        {
            _logger.LogWarning(ex, "Label photo could not be decoded");
            return new IdentifyLabelResponse(ErrorCodes.LabelPhotoUndecodable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Label OCR service failed");
            return new IdentifyLabelResponse(ErrorCodes.ExternalServiceError);
        }

        if (string.IsNullOrWhiteSpace(rawText))
        {
            _logger.LogInformation("Label OCR returned no readable ingredients");
            return new IdentifyLabelResponse(ErrorCodes.LabelTextUnreadable);
        }

        var normalized = LabelTextNormalizer.Normalize(rawText);
        var match = _matcher.Match(normalized);

        var candidates = new List<LabelCandidateDto>();
        foreach (var candidate in match.Candidates)
        {
            candidates.Add(new LabelCandidateDto
            {
                Family = candidate.Family,
                Score = Math.Round(candidate.Score, 1),
                Variants = await ResolveVariantsAsync(candidate.Codes, cancellationToken),
            });
        }

        _logger.LogInformation(
            "Label identified as {Decision} with top family {Family}",
            match.Decision,
            candidates.FirstOrDefault()?.Family ?? "none");

        return new IdentifyLabelResponse
        {
            RawText = rawText,
            Decision = match.Decision,
            Candidates = candidates,
        };
    }

    private async Task<List<LabelVariantDto>> ResolveVariantsAsync(
        IReadOnlyList<string> codes,
        CancellationToken cancellationToken)
    {
        var variants = new List<LabelVariantDto>();
        foreach (var code in codes)
        {
            // A code missing from the catalogue still yields the code — that is the
            // answer the operator needs; the name is a convenience.
            var product = await _catalogRepository.GetByIdAsync(code, cancellationToken);
            variants.Add(new LabelVariantDto
            {
                ProductCode = code,
                ProductName = product?.ProductName ?? string.Empty,
            });
        }

        return variants;
    }
}
```

- [ ] **Step 5: Write the module registration**

`LabelIdentificationModule.cs`:

```csharp
using Anela.Heblo.Application.Features.LabelIdentification.Services;
using Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;
using Anela.Heblo.Application.Shared;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.LabelIdentification;

public static class LabelIdentificationModule
{
    public static IServiceCollection AddLabelIdentificationModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LabelIdentificationOptions>(
            configuration.GetSection(LabelIdentificationOptions.SectionKey));

        // The index is immutable and parsed once from an embedded resource.
        services.AddSingleton<ILabelReferenceIndex, LabelReferenceIndex>();
        services.AddSingleton<ILabelMatcher, LabelMatcher>();
        services.AddScoped<ILabelOcrService, AnthropicLabelOcrService>();

        // Validators are registered explicitly per-module — this codebase has no
        // AddValidatorsFromAssembly.
        services.AddScoped<IValidator<IdentifyLabelRequest>, IdentifyLabelRequestValidator>();
        services.AddScoped<IPipelineBehavior<IdentifyLabelRequest, IdentifyLabelResponse>,
            ValidationBehavior<IdentifyLabelRequest, IdentifyLabelResponse>>();

        return services;
    }
}
```

- [ ] **Step 6: Register the module**

In `backend/src/Anela.Heblo.API/Program.cs`, find the block where other feature modules are registered (search for `Module(`) and add, matching the surrounding style:

```csharp
builder.Services.AddLabelIdentificationModule(builder.Configuration);
```

Add `using Anela.Heblo.Application.Features.LabelIdentification;` if the file uses explicit usings.

- [ ] **Step 7: Run tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~IdentifyLabelHandlerTests"
```

Expected: PASS (7 tests).

- [ ] **Step 8: Verify the response contract test still passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~Contract"
```

Expected: PASS — `IdentifyLabelResponse` inherits `BaseResponse`.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/LabelIdentification \
        backend/src/Anela.Heblo.API/Program.cs \
        backend/test/Anela.Heblo.Tests/Features/LabelIdentification/IdentifyLabelHandlerTests.cs
git commit -m "feat: add identify label use case"
```

---

### Task 9: Controller

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/LabelIdentificationController.cs`
- Test: `backend/test/Anela.Heblo.Tests/Controllers/LabelIdentificationControllerTests.cs`

**Interfaces:**
- Consumes: `IdentifyLabelRequest` / `IdentifyLabelResponse` (Task 8)
- Produces: `POST /api/label-identification/identify` accepting multipart field `photo`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Net.Http.Headers;
using Anela.Heblo.Application.Features.LabelIdentification.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class LabelIdentificationControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Mock<ILabelOcrService> _ocr = new();

    public LabelIdentificationControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddScoped(_ => _ocr.Object);
            }));
    }

    private static MultipartFormDataContent PhotoContent(byte[] bytes, string contentType = "image/jpeg")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "photo", "label.jpg" } };
    }

    [Fact]
    public async Task Identify_returns_ok_for_a_valid_photo()
    {
        _ocr.Setup(o => o.ReadIngredientsAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Tocopherol, Limonene");
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            "/api/label-identification/identify", PhotoContent(new byte[] { 1, 2, 3 }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Identify_rejects_a_request_with_no_file()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            "/api/label-identification/identify", new MultipartFormDataContent());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Identify_rejects_a_non_image_upload()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            "/api/label-identification/identify",
            PhotoContent(new byte[] { 1, 2, 3 }, "application/pdf"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Identify_rejects_an_empty_file()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            "/api/label-identification/identify", PhotoContent(Array.Empty<byte>()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

If the existing controller tests in this project use a different bootstrapping helper than `WebApplicationFactory<Program>` directly, match that helper instead — check a sibling file such as `ShipmentLabelsControllerTests.cs` first.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~LabelIdentificationControllerTests"
```

Expected: FAIL — 404, the route does not exist.

- [ ] **Step 3: Write the controller**

Follows the multipart pattern established by `CatalogDocumentsController.UploadPifDocument`.

```csharp
using Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Products_Catalog)]
[ApiController]
[Route("api/label-identification")]
public class LabelIdentificationController : BaseApiController
{
    private const int MaxUploadBytes = 10 * 1024 * 1024;

    private readonly IMediator _mediator;

    public LabelIdentificationController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Identifies a product from a photo of its etiquette. Labels print only the INCI
    /// composition, which identifies a product FAMILY — size variants share artwork text,
    /// so a family with two sizes returns both variants for the operator to choose from.
    /// </summary>
    [HttpPost("identify")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<IdentifyLabelResponse>> Identify(
        IFormFile? photo,
        CancellationToken ct = default)
    {
        if (photo is null || photo.Length == 0)
        {
            return BadRequest(new IdentifyLabelResponse(
                Application.Shared.ErrorCodes.LabelPhotoMissingOrInvalid));
        }

        await using var stream = photo.OpenReadStream();
        var response = await _mediator.Send(new IdentifyLabelRequest
        {
            PhotoStream = stream,
            ContentType = photo.ContentType ?? string.Empty,
            SizeBytes = photo.Length,
        }, ct);

        return HandleResponse(response);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~LabelIdentificationControllerTests"
```

Expected: PASS (4 tests).

- [ ] **Step 5: Run the whole backend suite and format**

```bash
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false
dotnet format Anela.Heblo.sln
```

Expected: all tests pass. (An `AccessMatrixGen` crash during the run is known non-fatal noise.)

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/LabelIdentificationController.cs \
        backend/test/Anela.Heblo.Tests/Controllers/LabelIdentificationControllerTests.cs
git commit -m "feat: add label identification controller"
```

---

### Task 10: Regenerate the TS client and add the frontend hook

**Files:**
- Modify: `frontend/src/api/generated/api-client.ts` (generated — do not hand-edit)
- Create: `frontend/src/api/hooks/useLabelIdentification.ts`
- Test: `frontend/src/api/hooks/__tests__/useLabelIdentification.test.ts`

**Interfaces:**
- Consumes: the generated `labelIdentification_Identify` method and `FileParameter`
- Produces: `useIdentifyLabelMutation()` returning a TanStack `useMutation` whose `mutateAsync(file: File)` resolves to `IdentifyLabelResponse`.

- [ ] **Step 1: Regenerate the client**

```bash
dotnet msbuild -t:GenerateFrontendClientManual backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
grep -n "labelIdentification_Identify" frontend/src/api/generated/api-client.ts
```

Expected: the generated method appears, accepting a `FileParameter`. Note its exact name and signature — use them verbatim in the next step. (`FileParameter` is already used by `catalogDocuments_UploadPifDocument`, so no generator configuration is needed.)

- [ ] **Step 2: Write the failing test**

```typescript
import { renderHook, waitFor } from "@testing-library/react";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useIdentifyLabelMutation } from "../useLabelIdentification";
import { getAuthenticatedApiClient } from "../../client";

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const wrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

describe("useIdentifyLabelMutation", () => {
  it("sends the photo as a FileParameter and returns the response", async () => {
    const identify = jest.fn().mockResolvedValue({
      success: true,
      decision: "Auto",
      rawText: "Tocopherol",
      candidates: [{ family: "KRE005", score: 100, variants: [] }],
    });
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      labelIdentification_Identify: identify,
    });

    const { result } = renderHook(() => useIdentifyLabelMutation(), { wrapper });
    const file = new File(["x"], "label.jpg", { type: "image/jpeg" });
    const response = await result.current.mutateAsync(file);

    await waitFor(() => expect(identify).toHaveBeenCalledTimes(1));
    expect(identify).toHaveBeenCalledWith({ data: file, fileName: "label.jpg" });
    expect(response.candidates[0].family).toBe("KRE005");
  });

  it("propagates API errors so the screen can show a Czech message", async () => {
    const identify = jest.fn().mockRejectedValue(new Error("boom"));
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      labelIdentification_Identify: identify,
    });

    const { result } = renderHook(() => useIdentifyLabelMutation(), { wrapper });
    const file = new File(["x"], "label.jpg", { type: "image/jpeg" });

    await expect(result.current.mutateAsync(file)).rejects.toThrow("boom");
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="useLabelIdentification" --watchAll=false
```

Expected: FAIL — module not found.

- [ ] **Step 4: Write the hook**

`frontend/src/api/hooks/useLabelIdentification.ts`:

```typescript
import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import { IdentifyLabelResponse } from "../generated/api-client";

/**
 * Identifies a product from a photo of its etiquette.
 *
 * Labels print only the INCI composition, which identifies a product FAMILY — size
 * variants share the same artwork text. A family with two sizes returns both variants
 * so the operator can pick the one in hand.
 */
export const useIdentifyLabelMutation = () =>
  useMutation<IdentifyLabelResponse, Error, File>({
    mutationFn: async (photo: File) => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.labelIdentification_Identify({
        data: photo,
        fileName: photo.name,
      });
    },
  });
```

If Step 1 reported a different generated method name or argument shape, use that instead — the generated client is the source of truth.

**Check how `LabelMatchDecision` was generated before writing Task 11.** `[JsonStringEnumConverter]` normally makes NSwag emit a string-valued enum, but generated enums have bitten this repo before (`CI=false npm run build` catches string-vs-numeric mismatches that `tsc --noEmit` misses). Run:

```bash
grep -n -A5 "enum LabelMatchDecision" frontend/src/api/generated/api-client.ts
```

If it emits an enum object, import and compare against `LabelMatchDecision.Auto` rather than the string literal `"Auto"` in Task 11, and update that task's test mocks to match.

- [ ] **Step 5: Run the test to verify it passes**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="useLabelIdentification" --watchAll=false
```

Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/generated/api-client.ts \
        frontend/src/api/hooks/useLabelIdentification.ts \
        frontend/src/api/hooks/__tests__/useLabelIdentification.test.ts
git commit -m "feat: add label identification client hook"
```

---

### Task 11: Terminal screen

**Files:**
- Create: `frontend/src/components/terminal/label-identification/LabelIdentificationScreen.tsx`
- Test: `frontend/src/components/terminal/label-identification/__tests__/LabelIdentificationScreen.test.tsx`

> **Corrected during implementation.** This task originally created a component-local
> `labelIdentificationErrors.ts` map. That was wrong: this repo already has a central
> `frontend/src/utils/errorHandler.ts` exposing `handleApiError(response: BaseResponse): string`,
> which resolves `errorCode` through the `errors.<EnumName>` i18n key. A backend test
> (`LocalizationCoverageTests.FrontendI18n_ShouldHaveTranslationsForAllErrorCodes`) *enforces* that
> every `ErrorCodes` member has a translation in `frontend/src/i18n.ts`, so a bespoke component map
> would both duplicate the strings and fail to satisfy the test. The Czech strings now live in
> `i18n.ts` (added in Task 9's fix round) and this screen calls `handleApiError`.

**Interfaces:**
- Consumes: `useIdentifyLabelMutation` (Task 10), `useScreenView` (existing)
- Produces: default-exported `LabelIdentificationScreen` React component.

- [ ] **Step 1: Write the failing tests**

```tsx
import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import LabelIdentificationScreen from "../LabelIdentificationScreen";
import { useIdentifyLabelMutation } from "../../../../api/hooks/useLabelIdentification";

jest.mock("../../../../api/hooks/useLabelIdentification");
jest.mock("../../../../telemetry/useScreenView", () => ({ useScreenView: jest.fn() }));

const mockMutate = jest.fn();
const setupMutation = (overrides = {}) => {
  (useIdentifyLabelMutation as jest.Mock).mockReturnValue({
    mutateAsync: mockMutate,
    isPending: false,
    ...overrides,
  });
};

const uploadPhoto = () => {
  const input = screen.getByTestId("label-photo-input");
  fireEvent.change(input, {
    target: { files: [new File(["x"], "label.jpg", { type: "image/jpeg" })] },
  });
};

beforeEach(() => {
  jest.clearAllMocks();
  setupMutation();
});

describe("LabelIdentificationScreen", () => {
  it("shows the capture button initially", () => {
    render(<LabelIdentificationScreen />);
    expect(screen.getByText("Vyfotit štítek")).toBeInTheDocument();
  });

  it("shows a single product code and name when one variant auto-confirms", async () => {
    mockMutate.mockResolvedValue({
      success: true,
      decision: "Auto",
      rawText: "…",
      candidates: [{
        family: "PEE002", score: 97.2,
        variants: [{ productCode: "PEE002015", productName: "Ochráním chodidla" }],
      }],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    await waitFor(() => expect(screen.getByText("PEE002015")).toBeInTheDocument());
    expect(screen.getByText("Ochráním chodidla")).toBeInTheDocument();
    expect(screen.queryByTestId("label-size-step")).not.toBeInTheDocument();
  });

  it("asks for the size when the family has two variants", async () => {
    mockMutate.mockResolvedValue({
      success: true,
      decision: "Auto",
      rawText: "…",
      candidates: [{
        family: "KRE005", score: 100,
        variants: [
          { productCode: "KRE005015", productName: "Masážní olej 15 ml" },
          { productCode: "KRE005030", productName: "Masážní olej 30 ml" },
        ],
      }],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    await waitFor(() => expect(screen.getByTestId("label-size-step")).toBeInTheDocument());
    fireEvent.click(screen.getByTestId("label-variant-KRE005030"));

    await waitFor(() => expect(screen.getByTestId("label-final-code")).toHaveTextContent("KRE005030"));
  });

  it("lists candidates to choose from on a Choose decision", async () => {
    mockMutate.mockResolvedValue({
      success: true,
      decision: "Choose",
      rawText: "…",
      candidates: [
        { family: "KRE005", score: 74.1, variants: [{ productCode: "KRE005015", productName: "A" }] },
        { family: "MAS007", score: 71.0, variants: [{ productCode: "MAS007015", productName: "B" }] },
      ],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    await waitFor(() => expect(screen.getByTestId("label-candidate-KRE005")).toBeInTheDocument());
    expect(screen.getByTestId("label-candidate-MAS007")).toBeInTheDocument();
  });

  it("shows the unreadable message with a retry on a Low decision", async () => {
    mockMutate.mockResolvedValue({
      success: true, decision: "Low", rawText: "…", candidates: [],
    });
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    await waitFor(() =>
      expect(screen.getByText("Nepodařilo se přečíst štítek")).toBeInTheDocument());
    expect(screen.getByText("Zkusit znovu")).toBeInTheDocument();
  });

  it("shows a Czech error message when the request fails", async () => {
    mockMutate.mockRejectedValue(new Error("boom"));
    render(<LabelIdentificationScreen />);
    uploadPhoto();

    await waitFor(() =>
      expect(
        screen.getByText("Služba rozpoznávání není dostupná, zkuste to znovu."),
      ).toBeInTheDocument());
  });

  it("shows a reading indicator while the request is in flight", () => {
    setupMutation({ isPending: true });
    render(<LabelIdentificationScreen />);
    expect(screen.getByText("Čtu štítek…")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="LabelIdentificationScreen" --watchAll=false
```

Expected: FAIL — module not found.

- [ ] **Step 3: Confirm the shared error helper and i18n keys are in place**

No new error-map file. Use the existing central helper:

```typescript
// frontend/src/utils/errorHandler.ts
export function handleApiError(response: BaseResponse): string;
```

It resolves `response.errorCode` through the `errors.<EnumName>` i18n key and returns a
ready-to-render Czech string. Verify the three keys exist (added in Task 9's fix round):

```bash
grep -n "LabelPhotoMissingOrInvalid\|LabelPhotoUndecodable\|LabelTextUnreadable" frontend/src/i18n.ts
```

Expected: three entries under the `errors` block. If any is missing, stop and report — the backend
`LocalizationCoverageTests` would also be failing.

For a thrown/network failure (no `BaseResponse` to inspect), use the `ExternalServiceError` key so
the operator still gets the "služba není dostupná" message rather than a raw exception string.

- [ ] **Step 4: Write the screen**

`frontend/src/components/terminal/label-identification/LabelIdentificationScreen.tsx`:

```tsx
import React, { useRef, useState } from "react";
import { Camera, RotateCcw } from "lucide-react";
import { useScreenView } from "../../../telemetry/useScreenView";
import { useIdentifyLabelMutation } from "../../../api/hooks/useLabelIdentification";
import {
  IdentifyLabelResponse,
  LabelCandidateDto,
  LabelVariantDto,
} from "../../../api/generated/api-client";
import { handleApiError } from "../../../utils/errorHandler";

type ScreenState =
  | { kind: "capture" }
  | { kind: "result"; response: IdentifyLabelResponse }
  | { kind: "chosen"; variant: LabelVariantDto }
  | { kind: "error"; message: string };

const LabelIdentificationScreen: React.FC = () => {
  useScreenView("Terminal", "LabelIdentification");

  const [state, setState] = useState<ScreenState>({ kind: "capture" });
  const [selectedFamily, setSelectedFamily] = useState<LabelCandidateDto | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const identify = useIdentifyLabelMutation();

  const reset = () => {
    setState({ kind: "capture" });
    setSelectedFamily(null);
    if (inputRef.current) inputRef.current.value = "";
  };

  const handlePhoto = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) return;

    try {
      const response = await identify.mutateAsync(file);
      if (!response.success) {
        setState({ kind: "error", message: handleApiError(response) });
        return;
      }
      // A family with exactly one variant needs no size step.
      const top = response.candidates?.[0];
      if (response.decision === "Auto" && top && top.variants.length === 1) {
        setState({ kind: "chosen", variant: top.variants[0] });
        return;
      }
      if (response.decision === "Auto" && top) {
        setSelectedFamily(top);
      }
      setState({ kind: "result", response });
    } catch {
      // Thrown/network failure — no BaseResponse to inspect, so surface the
      // generic upstream-unavailable message via the same i18n path.
      setState({
        kind: "error",
        message: handleApiError({ success: false, errorCode: ErrorCodes.ExternalServiceError }),
      });
    }
  };

  if (identify.isPending) {
    return (
      <Centered>
        <div className="h-12 w-12 animate-spin rounded-full border-4 border-primary-blue border-t-transparent" />
        <p className="mt-4 text-lg text-neutral-slate dark:text-graphite-text">Čtu štítek…</p>
      </Centered>
    );
  }

  if (state.kind === "chosen") {
    return (
      <Centered>
        <p
          data-testid="label-final-code"
          className="text-5xl font-extrabold text-emerald-600 dark:text-emerald-400"
        >
          {state.variant.productCode}
        </p>
        <p className="mt-3 text-xl text-neutral-slate dark:text-graphite-text">
          {state.variant.productName}
        </p>
        <ScanAgain onClick={reset} />
      </Centered>
    );
  }

  if (state.kind === "error") {
    return (
      <Centered>
        <p className="text-lg font-semibold text-rose-600 dark:text-rose-400">{state.message}</p>
        <ScanAgain onClick={reset} label="Zkusit znovu" />
      </Centered>
    );
  }

  if (state.kind === "result") {
    const { response } = state;

    if (selectedFamily) {
      return (
        <Centered>
          <p className="text-3xl font-extrabold text-neutral-slate dark:text-graphite-text">
            {selectedFamily.family}
          </p>
          <p className="mt-2 mb-6 text-base text-neutral-gray dark:text-graphite-muted">
            Vyberte velikost
          </p>
          <div data-testid="label-size-step" className="grid w-full max-w-md gap-4">
            {selectedFamily.variants.map((variant) => (
              <button
                key={variant.productCode}
                data-testid={`label-variant-${variant.productCode}`}
                onClick={() => setState({ kind: "chosen", variant })}
                className="rounded-2xl border border-border-light bg-white p-6 text-left shadow-soft transition-all hover:border-primary-blue dark:border-graphite-border dark:bg-graphite-surface"
              >
                <p className="text-2xl font-bold text-neutral-slate dark:text-graphite-text">
                  {variant.productCode}
                </p>
                <p className="text-sm text-neutral-gray dark:text-graphite-muted">
                  {variant.productName}
                </p>
              </button>
            ))}
          </div>
          <ScanAgain onClick={reset} />
        </Centered>
      );
    }

    const isLow = response.decision === "Low";
    return (
      <Centered>
        {isLow && (
          <p className="mb-4 text-lg font-semibold text-rose-600 dark:text-rose-400">
            Nepodařilo se přečíst štítek
          </p>
        )}
        {!isLow && (
          <p className="mb-4 text-base text-neutral-gray dark:text-graphite-muted">
            Vyberte produkt
          </p>
        )}
        <div className="grid w-full max-w-md gap-3">
          {(response.candidates ?? []).map((candidate) => (
            <button
              key={candidate.family}
              data-testid={`label-candidate-${candidate.family}`}
              onClick={() => setSelectedFamily(candidate)}
              className="rounded-2xl border border-border-light bg-white p-5 text-left shadow-soft transition-all hover:border-primary-blue dark:border-graphite-border dark:bg-graphite-surface"
            >
              <div className="flex items-baseline justify-between">
                <p className="text-xl font-bold text-neutral-slate dark:text-graphite-text">
                  {candidate.family}
                </p>
                <span className="text-sm text-neutral-gray dark:text-graphite-muted">
                  {candidate.score.toFixed(1)}
                </span>
              </div>
              <p className="text-sm text-neutral-gray dark:text-graphite-muted">
                {candidate.variants.map((v) => v.productName).filter(Boolean).join(" / ")}
              </p>
            </button>
          ))}
        </div>
        <ScanAgain onClick={reset} label={isLow ? "Zkusit znovu" : "Skenovat další"} />
      </Centered>
    );
  }

  return (
    <Centered>
      <label
        htmlFor="label-photo-input"
        className="flex w-full max-w-md cursor-pointer flex-col items-center gap-3 rounded-2xl bg-primary-blue p-10 text-white shadow-lg"
      >
        <Camera className="h-12 w-12" />
        <span className="text-2xl font-bold">Vyfotit štítek</span>
      </label>
      <input
        id="label-photo-input"
        data-testid="label-photo-input"
        ref={inputRef}
        type="file"
        accept="image/*"
        capture="environment"
        className="hidden"
        onChange={handlePhoto}
      />
    </Centered>
  );
};

const Centered: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <div className="flex h-full flex-col items-center justify-center p-4">{children}</div>
);

const ScanAgain: React.FC<{ onClick: () => void; label?: string }> = ({
  onClick,
  label = "Skenovat další",
}) => (
  <button
    onClick={onClick}
    className="mt-8 inline-flex items-center gap-2 text-base font-semibold text-primary-blue dark:text-graphite-accent"
  >
    <RotateCcw className="h-4 w-4" />
    {label}
  </button>
);

export default LabelIdentificationScreen;
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="LabelIdentificationScreen" --watchAll=false
```

Expected: PASS (7 tests).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/terminal/label-identification
git commit -m "feat: add label identification terminal screen"
```

---

### Task 12: Wire the route and the terminal tile, then validate

**Files:**
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/components/terminal/TerminalHome.tsx`
- Test: `frontend/src/components/terminal/__tests__/TerminalHome.test.tsx` (existing — extend)

**Interfaces:**
- Consumes: `LabelIdentificationScreen` (Task 11)
- Produces: route `/terminal/label-identification`, tile `label-id` on the terminal home.

- [ ] **Step 1: Write the failing test**

Add to the existing `TerminalHome` test file (match its existing import and render helpers):

```tsx
  it("offers the label identification workflow", () => {
    renderTerminalHome();

    const tile = screen.getByText("Identifikace štítku");
    expect(tile).toBeInTheDocument();
    expect(tile.closest("a")).toHaveAttribute("href", "/terminal/label-identification");
  });
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="TerminalHome" --watchAll=false
```

Expected: FAIL — the tile does not exist.

- [ ] **Step 3: Add the tile**

In `frontend/src/components/terminal/TerminalHome.tsx`, add `ScanText` to the existing `lucide-react` import and append to the `WORKFLOWS` array:

```tsx
  {
    id: 'label-id',
    title: 'Identifikace štítku',
    description: 'Vyfoťte štítek a zjistěte kód produktu',
    href: '/terminal/label-identification',
    icon: ScanText,
    comingSoon: false,
  },
```

- [ ] **Step 4: Add the route**

In `frontend/src/App.tsx`, add the import beside the other terminal imports:

```tsx
import LabelIdentificationScreen from "./components/terminal/label-identification/LabelIdentificationScreen";
```

and add the route inside the terminal `<Route>` block, beside `lot-identification`:

```tsx
                        <Route path="label-identification" element={<LabelIdentificationScreen />} />
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
cd frontend && CI=true npx react-scripts test --testPathPattern="TerminalHome" --watchAll=false
```

Expected: PASS.

- [ ] **Step 6: Full validation**

```bash
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false
cd frontend && CI=false npm run build && npm run lint && CI=true npx react-scripts test --watchAll=false
```

Expected: all green. `CI=false npm run build` is the real type gate — `npx tsc --noEmit` false-greens on react-i18next `.d.ts` parse errors.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/App.tsx \
        frontend/src/components/terminal/TerminalHome.tsx \
        frontend/src/components/terminal/__tests__/TerminalHome.test.tsx
git commit -m "feat: wire label identification route and terminal tile"
```

---

## Manual verification

Automated tests cover everything except the real vision call. After Task 12:

1. Set `Anthropic:ApiKey` in `backend/src/Anela.Heblo.API/secrets.json` (edit the JSON directly — do not use `dotnet user-secrets set`).
2. Run the backend and frontend, open `/terminal/label-identification` on a phone.
3. Photograph the massage-oil roll from `data/samples/photo.jpeg`. Expected: family `KRE005`, decision `Auto`, then a size step offering `KRE005015` and `KRE005030` with names.
4. Photograph the second sample (`data/samples/photo (1).jpeg`) and confirm it resolves to its own family, not `KRE005`.
5. Photograph something that is not a label. Expected: "Nepodařilo se přečíst štítek".

If step 3 returns `Choose` rather than `Auto`, the OCR transcription is degrading the score — check `rawText` in the response before touching thresholds. The matcher scores 100.0 on the reference text, so a low score is an OCR problem, not a matching one.
