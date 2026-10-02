---
process: flow-label-identification
kind: workflow
module: label-identification
summary: On-demand terminal flow that sends a photo of a product sticker to Claude vision to read its INCI list, fuzzy-matches the text against an embedded reference index of 25 families / 37 product codes, and returns up to 3 candidate products for the operator to confirm.
owns:
  - backend/src/Anela.Heblo.Application/Features/LabelIdentification/**
  - backend/src/Anela.Heblo.API/Controllers/LabelIdentificationController.cs
  - backend/tools/Anela.Heblo.LabelReferenceExtractor/**
  - scripts/render-label-references.sh
  - frontend/src/components/terminal/label-identification/**
  - frontend/src/api/hooks/useLabelIdentification.ts
verified_at: "5e993f9e2"
related: []
---

# Label identification (photo → product code)

## Purpose
Answers "which product is this unmarked roll of stickers for?". Anela's stickers print only
the INCI ingredient list; the operator photographs one on the warehouse terminal
(*Identifikace štítku*, `/terminal/label-identification`) and gets the product code and name.
The result is shown on screen only — nothing is saved, posted or printed.

A sticker identifies a product **family** (first 6 characters of the code, `KRE005015` →
`KRE005`); the `015`/`030` suffix is the sticker size and both sizes print identical text, so
for two-size families the operator picks the size.

## Trigger
On demand, one request per photo: the operator taps *Vyfotit štítek* (rear camera, or file
picker) → frontend `useIdentifyLabelMutation` → `POST /api/label-identification/identify`
(multipart, field `photo`). No Hangfire job, no BackgroundRefresh task.

Screen states (`LabelIdentificationScreen.tsx`): capture → *Čtu štítek…* →
- decision **Auto** + one-size family → final screen with the code (*chosen*);
- decision **Auto** + two-size family → size step (*Vyberte velikost*) → chosen;
- decision **Choose** → candidate list (*Vyberte produkt*, up to 3, with score and reference
  image) → size step if needed → chosen;
- decision **Low**, or an empty candidate list → *Nepodařilo se přečíst štítek* → retry;
- any error → Czech error message → *Zkusit znovu*.

## Data flow
**Per request**
1. Controller `LabelIdentificationController.Identify`: rejects a missing, empty or
   non-`image/*` upload with `LabelPhotoMissingOrInvalid` (3301, HTTP 400). Body cap
   `[RequestSizeLimit]` 25 MB; bigger uploads are cut off by Kestrel with HTTP 413 (the UI maps
   413 to the 3301 message).
2. `IdentifyLabelRequestValidator` (MediatR `ValidationBehavior`): `SizeBytes > 0` and content
   type starts with `image/`.
3. `AnthropicLabelOcrService.ReadIngredientsAsync`: SkiaSharp reads the image header; images
   over 50 MP are rejected; the photo is decoded, downscaled so the longest edge is ≤
   `LabelIdentification:MaxImageEdge` (2048 px), re-encoded as JPEG quality 90.
4. One call to the **Anthropic Messages API** through the shared `IChatClient`
   (`AnthropicChatClient`, `POST https://api.anthropic.com/v1/messages`): image block + a
   fixed English prompt asking for the INCI list of ONE label as a single comma-separated
   line, ignoring rotation, blur and text bleeding from neighbouring stickers, and returning
   nothing if illegible. Reply = first text block, trimmed.
5. Empty reply → `LabelTextUnreadable` (3303, HTTP 422).
6. `LabelTextNormalizer.Normalize` (same code the offline extractor uses).
7. `LabelMatcher.Match` scores the text against every entry of `LabelReferenceIndex`
   (in-memory, from the embedded `label-references.json`) and keeps the top 3.
8. `ICatalogRepository.GetByIdsAsync` — one bulk lookup of all candidate codes in the
   in-memory catalogue cache, for `ProductName`. A code not in the catalogue is still
   returned with an empty name.
9. Response `IdentifyLabelResponse`: `RawText` (the OCR text), `Decision`
   (`Auto`/`Choose`/`Low`), `Candidates[]` = `{Family, Score (1 decimal), Variants[{ProductCode,
   ProductName}]}`.

**Reference data (offline, manual, developer machine only)**
1. Source: print-ready sticker PDFs `data/labels/{productCode}.pdf` (gitignored, ~67 MB,
   not in the repo or on any server).
2. `dotnet run --project backend/tools/Anela.Heblo.LabelReferenceExtractor -- <pdfDir> <outputJson>`
   — PdfPig text of all pages → `LabelTextNormalizer` → group by the first 6 characters of the
   file name (family); the longest normalized text wins a tie → JSON `[{family, codes[],
   normalized}]`. Exit code 2 if any PDF had no text layer.
3. Output is committed as
   `backend/src/Anela.Heblo.Application/Features/LabelIdentification/Data/label-references.json`
   and embedded into the Application assembly (`<EmbeddedResource>` in the csproj); it takes
   effect on the next deploy.
4. `scripts/render-label-references.sh` (`DPI`, default 400; needs poppler + jq) renders each
   family's `codes[0]` PDF to `frontend/public/label-references/{family}.png` and, for
   multi-page PDFs, `{family}-{n}.png` — the reference images the operator compares against.

## Logic & formulas
- **Normalization** (both ends): join hyphenated line breaks; drop everything up to and
  including the first `Ingredients:`; drop the artwork job-name stamp
  (`anela_…<n>ml_kelimek-dno_<n>mm[-_](sandwich|bila-snimatelna)`, which carries the size);
  lowercase; replace every character outside `a-z 0-9 , space` with a space (Czech
  diacritics included); collapse whitespace; remove spaces before commas; trim.
- **Score per family** (0–100): `0.7 × TokenSetRatio + 0.3 × Jaccard`.
  `TokenSetRatio` = FuzzySharp `Fuzz.TokenSetRatio(ocrText, referenceText)` (word level,
  robust to duplicated and re-ordered text). Jaccard = |A∩B| / |A∪B| × 100 over the
  **comma-separated ingredient sets** (exact string equality per ingredient).
- Ranking: score descending, then family code ascending; top 3 returned.
- **Decision** (`LabelMatcher.Decide`):
  - `Auto` — best ≥ `AutoConfirmScore` (90) **and** best − runner-up ≥ `AutoConfirmMargin` (5);
  - `Choose` — otherwise, if best ≥ `LowConfidenceFloor` (60);
  - `Low` — best < 60, or empty normalized text.
- Matching is by family, so the 12 two-size families (KRE001–007, OCH009, PMA001–004) never
  tie on size; the size is always the operator's choice.
- **Errors** (`ErrorCodes`, Czech text from `frontend/src/i18n.ts`):

| Code | HTTP | When | Operator sees |
|---|---|---|---|
| `LabelPhotoMissingOrInvalid` 3301 | 400 | no file / empty / not `image/*`; also 413 too large | *Nahrajte prosím fotku štítku.* |
| `LabelPhotoUndecodable` 3302 | 400 | SkiaSharp cannot decode, > 50 MP, resize fails | *Nepodařilo se načíst fotku.* |
| `LabelTextUnreadable` 3303 | 422 | model returned no text | *Na fotce nejsou čitelné ingredience — jděte blíž a držte telefon v klidu.* |
| `LabelOcrServiceUnavailable` 3304 | 503 | any other exception from the OCR call (HTTP error, timeout, missing API key) | *Služba rozpoznávání není dostupná, zkuste to znovu.* |

  A cancelled request (operator left the page) is re-thrown, not mapped to 3304.
- Anthropic call resilience (adapter-wide): up to 3 retries with exponential backoff (2 s base,
  honours `Retry-After`) on HTTP 429 and 529 only; HTTP client timeout
  `Anthropic:HttpTimeoutSeconds`.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `LabelIdentification:AutoConfirmScore` | 90 (code default; no appsettings entry) | Minimum best score for `Auto` |
| `LabelIdentification:AutoConfirmMargin` | 5 (code default) | Required lead over the runner-up for `Auto` |
| `LabelIdentification:LowConfidenceFloor` | 60 (code default) | Below this the result is `Low` |
| `LabelIdentification:MaxImageEdge` | 2048 (code default) | Longest edge in px sent to the model |
| `KnowledgeBase:ChatModel` | `claude-sonnet-4-6` | Model used for the OCR call (shared with the knowledge base chat) |
| `KnowledgeBase:ChatMaxTokens` | 1024 | Max output tokens of the OCR reply (shared) |
| `Anthropic:ApiKey` | empty (Key Vault) | Anthropic API key; empty → every photo fails with 3304 |
| `Anthropic:HttpTimeoutSeconds` | 180 | HTTP timeout of the Anthropic client |

Hard-coded: upload cap 25 MB (`MaxUploadBytes`), 50 MP decode cap, JPEG quality 90, score
weights 0.7/0.3, 3 candidates.

## Runtime facts
- At design time (2026-08-03) a blurry, rotated massage-oil photo scored 100.0 for `KRE005`
  with the runner-up (`MAS007`) 12 points behind; before family grouping, 11 size pairs were
  byte-identical so `Auto` could never fire for them — source:
  `docs/superpowers/specs/2026-08-03-label-identification-terminal-design.md` — 2026-08-03.

## Known quirks
- **Static reference set**: only the 37 codes in `label-references.json` can be identified.
  New products or artwork changes need the offline extractor (PDFs exist only on the
  developer's machine) plus a redeploy; the catalogue is never used to build the index.
- **A wrong size is not caught**: the sticker carries no size information, so the operator's
  015/030 choice is trusted.
- **Shared model setting**: the OCR follows `KnowledgeBase:ChatModel`/`ChatMaxTokens`; changing
  the knowledge-base chat model silently changes the label reader. A very long INCI list could
  be cut at 1024 output tokens; the fuzzy match usually still finds the family.
- **Permission mismatch**: the endpoint needs Katalog read (`Products_Catalog`) while the
  Terminal menu needs `Warehouse_Logistics`; a terminal user without Katalog read gets HTTP 403,
  which the UI cannot parse and shows as *Služba rozpoznávání není dostupná* (3304 text) —
  misleading, it is a permissions problem.
- `LabelIdentificationOptions` has no section in `appsettings*.json`; thresholds are tuned only
  via code defaults unless someone adds the section.
- Reference images: a family whose PNG is missing simply shows no image (`onError` hides it);
  single-page families request `{family}-2.png` in the zoom viewer and get a 404 by design.
- Nothing is persisted: no audit of who identified what; only application logs
  ("Label identified as {Decision} with top family {Family}").

## Code entry points
- `backend/src/Anela.Heblo.API/Controllers/LabelIdentificationController.cs` — endpoint, upload cap, permission
- `backend/src/Anela.Heblo.Application/Features/LabelIdentification/UseCases/IdentifyLabel/IdentifyLabelHandler.cs` — orchestration and error mapping
- `backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/AnthropicLabelOcrService.cs` — image downscale + prompt
- `backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/LabelTextNormalizer.cs` — normalization rules
- `backend/src/Anela.Heblo.Application/Features/LabelIdentification/Services/LabelMatcher.cs` — scoring and decision
- `backend/src/Anela.Heblo.Application/Features/LabelIdentification/LabelIdentificationOptions.cs` — thresholds
- `backend/tools/Anela.Heblo.LabelReferenceExtractor/Program.cs` — offline index builder
- `scripts/render-label-references.sh` — reference PNG renderer
- `frontend/src/components/terminal/label-identification/LabelIdentificationScreen.tsx` — UI states and error mapping
