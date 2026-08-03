# Label Identification in the Terminal — Design

**Date:** 2026-08-03
**Status:** Approved
**Supersedes:** `2026-08-03-label-ocr-product-identifier-design.md` (standalone Python tool)

## Problem

Anela Heblo's product etiquettes — round stickers sold on rolls — print only the INCI
ingredient composition. No product code, no name, no barcode. Identifying an unlabelled
roll means comparing ingredient text against composition records by hand: slow and
error-prone.

The tool: photograph a label in the terminal app, get the product code and name back.

## What changed from the standalone design

The prior design specified a one-shot Python/FastAPI app in `tools/label-identifier/`,
explicitly out of the Heblo monorepo. This design puts it inside Heblo, in the terminal
app. Two things drove the revision.

### The catalogue makes the original output contract unachievable

All 37 reference PDFs were extracted and scored pairwise with the prior design's own
formula. **Eleven pairs are byte-identical after normalization:**

```
KRE001015 ≡ KRE001030    PMA001015 ≡ PMA001030
KRE002015 ≡ KRE002030    PMA002015 ≡ PMA002030
KRE004015 ≡ KRE004030    PMA003015 ≡ PMA003030
KRE005015 ≡ KRE005030    PMA004015 ≡ PMA004030
KRE006015 ≡ KRE006030    OCH009015 ≡ OCH009030
KRE007015 ≡ KRE007030
```

`KRE003015` vs `KRE003030` scores 98.5. The `015`/`030` suffix is the sticker size —
confirmed in PDF page geometry (137.717 pt ≈ 48.6 mm vs 151.89 pt ≈ 53.6 mm), not in the
ingredient text. The same INCI composition is printed on both sizes.

Consequence: the `AUTO` rule (`best ≥ 90` **and** `margin ≥ 5`) can never fire for 24 of
37 labels, because the margin is exactly 0. Verified end-to-end against the massage-oil
sample photo:

```
100.0 (tsr 100.0 / jac 100.0)  KRE005030
100.0 (tsr 100.0 / jac 100.0)  KRE005015     <- margin 0.0, AUTO = false
 87.7 (tsr  95.8 / jac  69.0)  MAS007015
 71.8                          OCH009015/030
```

The matching layer is sound — a perfect 100.0 on a blurry, rotated, ghost-texted phone
photo with the runner-up 12 points back. Only the output contract was wrong: **a label
identifies the product family, never the size variant.** No OCR or threshold change fixes
this; the information is not printed on the sticker.

**Resolution: index by family, prompt for size.** Family = the first six characters of the
product code (`KRE005015` → `KRE005`).

| | |
|---|---|
| 37 labels | → 25 families |
| 13 families | single size — resolve in zero taps |
| 12 families | two sizes — one extra tap |
| worst family-level pair | `MAS007` vs `KRE005` at 87.7 |

At family level there are no ties, so a perfect match clears the margin by 12.3 in the
worst case. **The prior design's thresholds (90 / 5 / 60) survive unchanged** — they were
being applied to the wrong unit, not miscalibrated.

### Heblo already provides most of the infrastructure

| Prior design's module | Heblo equivalent |
|---|---|
| `ocr.py` + Anthropic SDK | `Anela.Heblo.Adapters.Anthropic` (Polly retry on 429/529) |
| `config.py` frozen dataclass | `AnthropicOptions` + Key Vault |
| `data/labels/*.pdf` at runtime | committed extracted text (see below) |
| `log_store.py` JSONL | App Insights telemetry + `ILogger` |
| `static/index.html` vanilla JS | terminal `TerminalLayout` / `useScreenView` |
| no auth ("obscure URL") | Entra auth already covers the terminal |
| separate `uvicorn` deployment | ships in the existing container |

One genuine gap: `AnthropicChatClient` is text-only. See §3.

## Scope

A `LabelIdentification` vertical slice in `Anela.Heblo.Application`, one controller, and
one terminal workflow. No Python, no second deployment, no new infrastructure.

## 1. Reference data — committed, no runtime PDF parsing

Nothing needs the PDFs at request time. The only input matching consumes is the normalized
INCI string: ~700 bytes per product, ~27 KB for the whole catalogue.

One committed file:

```
backend/src/Anela.Heblo.Application/Features/LabelIdentification/Data/label-references.json
```

```json
[
  { "family": "KRE005", "codes": ["KRE005015", "KRE005030"],
    "normalized": "prunus amygdalus dulcis oil, moringa oleifera seed oil, ..." },
  { "family": "PEE002", "codes": ["PEE002015"], "normalized": "..." }
]
```

25 entries, loaded as an embedded resource at startup into an immutable index.

All 12 multi-size families normalize to byte-identical text, so any member is a valid
representative; the extractor picks the longest as a defensive tie-break.

> **Corrected during implementation.** This section originally claimed `KRE003` was the one
> family whose sizes differed in text (99.9 similarity). That difference was entirely an
> artifact of the job-name stamp described under Normalization below — once the stamp is
> stripped, `KRE003`'s two sizes are identical like every other family. Verified per-code
> across all 37 PDFs.

**Generation is a one-time offline step.** A small console tool at
`backend/tools/LabelReferenceExtractor/` reads `data/labels/*.pdf` with **PdfPig 0.1.9**
(already referenced in the backend), applies the same normalizer as the runtime path, groups
by family, and writes the JSON. Committed so regeneration is reproducible when artwork
changes.

**The 37 source PDFs are gitignored, not committed.** `data/` is currently untracked and
the PDFs total ~67 MB; git history is permanent. The extracted 27 KB is the reference
data — the PDFs are only its source. Add `data/` to `.gitignore`.

Dropped along with runtime parsing: blob storage, the Hangfire refresh job, `IndexFailure`
handling, the `/health` index report, and the empty-index 503 path. An embedded resource
cannot be missing at runtime; if it fails to parse, startup fails loudly, which is correct.

## 2. Backend slice

```
Application/Features/LabelIdentification/
  LabelIdentificationModule.cs          DI + MediatR registration
  Data/label-references.json            embedded resource
  Services/LabelTextNormalizer.cs       pure: text -> canonical form
  Services/LabelMatcher.cs              pure: normalized + index -> ranked families
  Services/LabelReferenceIndex.cs       embedded JSON -> immutable index
  Services/ILabelOcrService.cs          image bytes -> INCI line
  Services/AnthropicLabelOcrService.cs  implementation
  UseCases/IdentifyLabel/
    IdentifyLabelRequest.cs
    IdentifyLabelRequestValidator.cs
    IdentifyLabelHandler.cs
    IdentifyLabelResponse.cs
    LabelCandidateDto.cs
    LabelVariantDto.cs
```

`LabelTextNormalizer` is load-bearing: both the offline extractor and the OCR path run
through it, so it is one implementation with its own tests rather than parallel logic that
can drift. It and `LabelMatcher` are pure — no network, no filesystem, fully deterministic —
and carry the test weight.

**Module registration follows the repo's per-module convention:** validators are registered
explicitly alongside `ValidationBehavior` in `LabelIdentificationModule`. There is no
`AddValidatorsFromAssembly` in this codebase.

### Normalization

Lowercase; join hyphenation across line breaks; strip everything up to and including the
`Ingredients:` prefix; normalize en-dash to hyphen and `/` to space; drop characters outside
`[a-z0-9, ]`; collapse whitespace.

**The prefix strip is load-bearing, not cosmetic.** The artwork PDFs carry a Czech job-name
line *above* the sticker's die-cut area — `Anela_Něžná paní Ovesná_15`,
`Anela_Malá čarodějka_15ml_k`, `Anela_Klidné nožky_15ml_kelim` — which is never printed on
the physical sticker and therefore never seen by OCR. Left in, it would pollute every index
entry with tokens the query can never match, systematically depressing scores. It also
carries the size (`_15ml_`), so leaving it in would break the family-identity property that
this whole design rests on.

**A prefix strip alone is not sufficient — corrected during implementation.** The PDFs are
**multi-page**, and the stamp repeats *once per page*. Under PdfPig's content-stream
ordering it lands *after* the ingredient text, where no prefix rule can reach it. (The
original corpus analysis used `pdftotext`, which orders by visual layout and placed the
stamp first — hence the wrong conclusion. `pdftotext` surfaces only 16 stamp occurrences
across the corpus; PdfPig sees 50, one per page.)

The normalizer therefore strips the stamp **wherever it occurs**, before the charset filter,
in addition to the `Ingredients:` prefix strip. The prefix strip is still required and still
lazy: multi-page PDFs carry the marker only on page 1, so `^.*?ingredients\s*:` correctly
preserves page 2's continuation of the ingredient list.

The extractor and the OCR path must call the same normalizer; this is why it is one
implementation.

The job-name line is *not* used as a product-name source. Names come from the catalogue,
which is authoritative and stays current.

### Matching

Unchanged from the prior design, applied to families:

```
score = 0.7 * token_set_ratio + 0.3 * (jaccard * 100)
```

`token_set_ratio` is robust against duplicated ghost text and reordering — precisely the
failure mode of photographing a roll. Jaccard over the comma-split ingredient sets sees
ingredient *boundaries* that word-level matching ignores. Both weights are named constants.

.NET has no RapidFuzz. **FuzzySharp** — a port of the same algorithm — is the one new NuGet
package. Both weights and all thresholds are named constants, overridable through
`LabelIdentificationOptions`.

| Condition | Decision |
|---|---|
| best ≥ 90 **and** (best − second) ≥ 5 | `Auto` |
| best ≥ 60 | `Choose` |
| best < 60 | `Low` |

### Contracts

DTOs are **classes, never records** — the OpenAPI generator mishandles record parameter
order. `IdentifyLabelResponse` inherits `BaseResponse`; the repo has a reflection contract
test that fails in CI otherwise.

```csharp
public class IdentifyLabelResponse : BaseResponse
{
    public string RawText { get; set; } = string.Empty;
    public LabelMatchDecision Decision { get; set; }        // Auto | Choose | Low
    public List<LabelCandidateDto> Candidates { get; set; } = new();   // top 3
}

public class LabelCandidateDto
{
    public string Family { get; set; } = string.Empty;
    public double Score { get; set; }
    public List<LabelVariantDto> Variants { get; set; } = new();
}

public class LabelVariantDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
}
```

`ProductName` is resolved per variant through `ICatalogRepository.GetByIdAsync(code)`
(`IReadOnlyRepository<CatalogAggregate, string>`, keyed by product code). A code absent from
the catalogue yields an empty name rather than failing the request — the code is still the
answer the operator needs.

`GetProductComposition` is **not** reusable as reference data: it returns the Flexi BoM
(material codes and grams), not the printed INCI text.

### Endpoint

```
POST /api/label-identification/identify
Content-Type: multipart/form-data, field: photo
```

Follows the established multipart pattern from
`CatalogDocumentsController.UploadPifDocument`: `IFormFile? photo` parameter, a
`[RequestSizeLimit]` attribute, `photo.OpenReadStream()`, and a MediatR request carrying
`FileStream` / `ContentType` / `SizeBytes`. NSwag already emits `FileParameter` for these,
so the generated TypeScript client handles the upload with no special casing.

Authorization: `[FeatureAuthorize(Feature.Products_Catalog)]` — read-level, reusing the
existing feature. `Feature` is generated from `access-matrix.json` by
`Anela.Heblo.AccessMatrixGen`; adding a new value would mean regenerating
`Feature.generated.cs`, `access-matrix.generated.json`,
`access-matrix-entra.generated.json`, and `frontend/src/auth/accessMatrix.generated.ts`,
and updating `AccessMatrixTests`. Label identification is a read-only product lookup, so
`Products_Catalog` fits and that regeneration is avoided.

Errors return `BaseResponse` with an `ErrorCodes` value; Czech strings live in the frontend
error map like every other module, not baked into HTTP bodies.

Each situation needs a **distinct** code — the frontend error map keys on it, so reusing the
generic `ValidationError` for three different failures would make them indistinguishable on
the phone. `ErrorCodes` uses per-module blocks (`32XX` = Authorization is currently the
highest); **`33XX` is the next free block:**

```csharp
// Label identification module errors (33XX)
[HttpStatusCode(HttpStatusCode.BadRequest)]
LabelPhotoMissingOrInvalid = 3301,
[HttpStatusCode(HttpStatusCode.BadRequest)]
LabelPhotoUndecodable = 3302,
[HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
LabelTextUnreadable = 3303,
[HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
LabelOcrServiceUnavailable = 3304,
```

| Situation | `ErrorCodes` | HTTP | Czech (frontend) |
|---|---|---|---|
| Missing / non-image / oversized upload | `LabelPhotoMissingOrInvalid` | 400 | "Nahrajte prosím fotku štítku." |
| Image cannot be decoded | `LabelPhotoUndecodable` | 400 | "Nepodařilo se načíst fotku." |
| Anthropic error or timeout | `LabelOcrServiceUnavailable` | 503 | "Služba rozpoznávání není dostupná, zkuste to znovu." |
| Model returns nothing readable | `LabelTextUnreadable` | 422 | "Na fotce nejsou čitelné ingredience — jděte blíž a držte telefon v klidu." |

> **Corrected during implementation.** The OCR-outage row originally mapped to the
> *shared* `ErrorCodes.ExternalServiceError` (9001) while promising feature-specific Czech
> copy. Those are incompatible: `ExternalServiceError`'s translation is the generic
> cross-module "Chyba externí služby", so the intended sentence could only be delivered by
> hardcoding it in the component — duplicating the error vocabulary and escaping the
> `LocalizationCoverageTests` gate, which only sees strings bound to an `ErrorCodes` member.
> A dedicated `3304` fixes it for the same reason 3301–3303 exist.

Two enforcement points apply whenever a code is added here, both learned the hard way:
`ErrorHandlingTests.ErrorCodes_ShouldFollowModulePrefixSystem` requires the `33XX` range to be
a declared bucket *and* folded into its aggregate count, and
`LocalizationCoverageTests.FrontendI18n_ShouldHaveTranslationsForAllErrorCodes` requires a
translation in `frontend/src/i18n.ts` for **every** member. Frontend code reads these through
the shared `handleApiError` helper (`frontend/src/utils/errorHandler.ts`), never a
component-local map.

Stack traces never reach the phone; full detail goes to `ILogger`.

## 3. Vision — extending the Anthropic adapter

`AnthropicChatClient.GetResponseAsync` currently flattens every message with
`content = m.Text`, so it is text-only and cannot carry an image.

Change: when a `ChatMessage` carries `DataContent`, emit Anthropic image content blocks
instead of a bare string. Contained to `BuildRequestBody` and the request DTOs. The
`IChatClient` public surface, the Polly pipeline, and every existing caller are unchanged —
and Photobank, Article, and RAG gain vision as a side effect.

Downscaling to 2048 px longest edge uses **SkiaSharp 2.88.3**, already referenced. Encode
JPEG before sending.

`ILabelOcrService` keeps the model behind an interface so tests never touch the network.

The prompt is constrained: return the ingredient list of **one** label as a single
comma-separated line. Labels on a roll are all the same product; rotation, blur, and ghost
text bleeding in from neighbouring stickers are expected and harmless.

### Configuration

`LabelIdentificationOptions`, bound from `appsettings.json`, all optional with defaults:

| Key | Default | Purpose |
|---|---|---|
| `AutoConfirmScore` | `90` | Threshold for `Auto` |
| `AutoConfirmMargin` | `5` | Required lead over the runner-up |
| `LowConfidenceFloor` | `60` | Below this, `Low` |
| `MaxImageEdge` | `2048` | Downscale target, px |

> The upload size cap (`25 * 1024 * 1024` — 25 MB, raised from an initial 10 MB that was
> below real phone-photo sizes) lives as a `const` on
> `LabelIdentificationController.MaxUploadBytes`, not in `LabelIdentificationOptions`: it is
> a `[RequestSizeLimit]` attribute argument, and attribute arguments must be compile-time
> constants, so it cannot read configuration. An earlier `MaxUploadBytes` option property
> was removed — it was read by nothing and setting it in configuration silently did
> nothing.

The model id and API key come from the existing `AnthropicOptions` (Key Vault). No new
secret.

## 4. Terminal UI

One tile appended to the existing `WORKFLOWS` array in `TerminalHome.tsx`, matching the
shape of its neighbours:

```ts
{ id: 'label-id', title: 'Identifikace štítku',
  description: 'Vyfoťte štítek a zjistěte kód produktu',
  href: '/terminal/label-identification', icon: ScanText, comingSoon: false }
```

Screens under `frontend/src/components/terminal/label-identification/`, routed in `App.tsx`
beside the `lot-identification` routes, inside `TerminalLayout`, with `useScreenView`
telemetry like every other terminal screen.

`lot-identification` has no per-route RBAC guard; these routes match that. The terminal is
already behind Entra auth.

### States

1. **Capture** — large button, `<input type="file" accept="image/*" capture="environment">`
2. **Reading** — spinner, "Čtu štítek…"
3. **Result**
   - `Auto`, single-size family — product code very large, product name below, green. Done.
   - `Auto`, two-size family — family and name, then two large size buttons (`15 ml` / `30 ml`)
   - `Choose` — top-3 families with scores and names; picking one falls through to the size
     step when that family has two sizes
   - `Low` — "nepodařilo se přečíst štítek" and retry, candidates available below
4. "Skenovat další" returns to state 1

The size step appears only for the 12 two-size families. Sizes are derived from the returned
`variants`, not hardcoded.

Designed for a phone held one-handed on a warehouse floor: large tap targets, high contrast,
the product code as the biggest thing on screen.

> **Added after initial ship — visual confirmation.** The result branches (candidate list,
> size step, final code) show the **reference label artwork** for the proposed family so the
> operator confirms by visual similarity to the sticker in hand, and can tap to enlarge and
> read the INCI text. This does **not** reintroduce blob storage or runtime PDF parsing: one
> PNG per family (25, page-1; long lists add a `{family}-2.png`) is **pre-rendered offline**
> by `scripts/render-label-references.sh` from the same gitignored `data/labels/*.pdf` the
> text extractor reads, and **committed as static assets** under
> `frontend/public/label-references/`. The frontend derives the image path from the
> `family` already on `LabelCandidateDto` — no backend, DTO, endpoint, or storage change.
> The committed PNGs (~3 MB total) are a deliberate exception to §1's "PDFs absent from git":
> the 67 MB of source PDFs stay gitignored; only the small rendered images are tracked.

### Data access

The hook calls the **generated typed client**, not `(apiClient as any).http.fetch` — recent
arch-review commits have been removing exactly that anti-pattern. URLs are absolute,
`${apiClient.baseUrl}${relativeUrl}`; a relative URL hits port 3001 instead of 5001.

## 5. Testing

Target 80%+ coverage. Moq + FluentAssertions, matching the `Anela.Heblo.Tests` convention.

**`LabelTextNormalizerTests`** — prefix stripping, hyphenation across line breaks,
whitespace collapsing, casing, punctuation, en-dash and slash handling.

**`LabelMatcherTests`** — seeded with the **real 25-family index**, so the tests encode
actual catalogue behaviour:
- exact reference text → `Auto` with the right family
- the `KRE005` / `MAS007` pair (87.7) as the confusability regression — a perfect `KRE005`
  match must still clear the margin
- mangled OCR: dropped characters, appended ghost text, reordered ingredients → still the
  right family
- garbage input → `Low`
- a two-size family returns both variants; a single-size family returns one

**`LabelReferenceIndexTests`** — the embedded resource parses, yields 25 families, and
every `codes` entry maps back to its family prefix.

**`IdentifyLabelHandlerTests`** — faked `ILabelOcrService` and `ICatalogRepository`;
asserts variant expansion, product-name resolution, the missing-catalogue-entry fallback,
and each decision branch.

**Controller integration test** — `WebApplicationFactory` with OCR faked; multipart upload,
oversized upload rejected, non-image rejected. Four `IFormFile` endpoints already exist
(`CatalogDocuments` ×2, `KnowledgeBase`, `Leaflet`); follow their test setup.

**Frontend** — `react-scripts test` (not `npx jest`). Component tests for the three result
states and the size-selection step. Terminal shell components mock their context
dependencies; new contexts must be mocked or sibling tests break.

**No E2E.** The nightly suite runs against deployed staging and camera capture is not
automatable.

## 6. Non-functional

- End-to-end ≤ ~10 s on mobile data, dominated by upload and the vision call
- One vision call per photo; no other paid service
- iOS Safari and Android Chrome; no app install
- Reference index is immutable and in-memory; matching is O(25) string comparisons

## 7. Out of scope

- **The learning cache.** With 25 families matched against exact artwork text, near-
  everything auto-confirms on the first pass. The cache buys one tap and costs a schema, a
  second threshold, a lookup path, and its own tests. Revisit if telemetry shows recurring
  manual confirmations.
- **Excel reference data.** PDFs are the single source; product names come from the catalogue.
- **Local/offline OCR.** The matcher is engine-agnostic behind `ILabelOcrService`.
- **Label detection, cropping, perspective correction.** The vision model handles these.
- **Editing reference data through the UI.** Regeneration is an offline step.
- **Writing identifications back to stock, lots, or manufacture records.** Read-only lookup.
- **Distinguishing sizes automatically.** Not printed on the label; the operator taps.

## 8. Success criteria

- Both sample photos resolve to the correct **family**; `KRE005` returns both variants with
  names, and the operator's size tap yields `KRE005015` or `KRE005030`
- A single-size family (e.g. `PEE002`, `MAS001`) resolves to a full product code in zero taps
- The `KRE005` / `MAS007` pair does not produce a confident wrong answer under degraded OCR
- Garbage input returns `Low`, never a confident code
- `dotnet build`, `dotnet format`, `CI=false npm run build`, `npm run lint` all pass
- The 37 PDFs are absent from git history
