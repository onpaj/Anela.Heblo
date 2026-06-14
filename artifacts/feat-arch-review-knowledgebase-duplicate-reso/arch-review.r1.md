I have enough context to write the architecture review.

```markdown
# Architecture Review: Consolidate Duplicate `ResolveContentType` Logic in KnowledgeBase

## Skip Design: true

This is a pure backend internal refactor — no UI components, screens, layouts, or visual decisions are involved. No frontend file is touched, no public API or DTO changes, no OpenAPI client regeneration is needed.

## Architectural Fit Assessment

The proposal aligns cleanly with the project's Vertical Slice + Clean Architecture conventions documented in `docs/architecture/filesystem.md`:

- **Module-scoped helper, not cross-module.** Both consumers (`UploadDocumentHandler`, `IndexDocumentHandler`) live under `Application/Features/KnowledgeBase/UseCases/`. A shared helper at `Application/Features/KnowledgeBase/ContentTypeResolver.cs` keeps the change inside the vertical slice — no leakage into `Shared/`, `Domain/`, or another feature.
- **`internal static` matches existing precedent.** The only other static class in the module today is `KnowledgeBaseModule` (DI registration). Pure-function helpers are an established pattern; nothing in the module-boundary rules prohibits this addition.
- **No DI footprint.** Resolution is a pure, deterministic mapping with no dependencies, no I/O, no logging — registering it in DI would be over-engineering. Spec correctly calls this out.
- **MediatR flow is preserved.** The proposal does not change the request DTO, the handler signatures, or the MediatR pipeline. Behavior at both entry points (HTTP upload via `KnowledgeBaseController` and direct `IMediator.Send` from `KnowledgeBaseIngestionJob`) remains observationally identical for currently supported extensions.

**Confirmed by code inspection:**
- `UploadDocumentHandler.cs` lines 73–84 and `IndexDocumentHandler.cs` lines 141–152 are byte-identical implementations.
- No other call site invokes the private methods.
- The `KnowledgeBaseIngestionJob` does not contain its own copy — it goes through `IndexDocumentHandler` via MediatR.
- Test infrastructure already mocks `IMediator` in `UploadDocumentHandlerTests` and verifies the resolved `ContentType` on the dispatched `IndexDocumentRequest` — these tests cover the cross-handler contract and will catch any behavioral drift.

**One discovery worth flagging (out of scope but related):** an identical fourth copy of `ResolveContentType` lives in `Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs` lines 73–84. The spec explicitly scopes this refactor to KnowledgeBase, so we will not touch Leaflet here, but the `internal` visibility choice (assembly-wide) leaves the door open for a follow-up that consolidates Leaflet's copy too. See *Specification Amendments* below.

## Proposed Architecture

### Component Overview

```
HTTP upload                       Background ingestion
     │                                     │
     ▼                                     │
┌────────────────────────────┐             │
│ UploadDocumentHandler      │             │
│  - reads stream            │             │
│  - resolves content type ──┼───┐         │
│  - checks extractor support│   │         │
│  - dispatches IndexDocReq  │   │         │
└────────────┬───────────────┘   │         │
             │ IMediator.Send    │         │
             ▼                   │         ▼
┌─────────────────────────────┐  │  ┌──────────────────────────┐
│ IndexDocumentHandler        │  │  │ KnowledgeBaseIngestionJob│
│  - resolves content type ───┼──┤  │  - builds IndexDocReq    │
│  - hashes / dedupes         │  │  │  - calls IMediator.Send  │
│  - persists + indexes       │  │  └────────────┬─────────────┘
└─────────────────────────────┘  │               │
             ▲                   │               │
             │                   │               │
             └───────────────────┴───── calls ───┘
                                 │
                                 ▼
                       ┌─────────────────────────┐
                       │ ContentTypeResolver     │
                       │  (internal static)      │
                       │  Resolve(ct, filename)  │
                       │  — pure function        │
                       └─────────────────────────┘
```

Two entry points, one resolver. In the upload flow, `Resolve` runs twice (once before dispatch in `UploadDocumentHandler`, once inside `IndexDocumentHandler`); idempotency makes the second call a no-op. In the ingestion-job flow, `Resolve` runs exactly once inside `IndexDocumentHandler`.

### Key Design Decisions

#### Decision 1: Where to place `ContentTypeResolver`
**Options considered:**
- (a) `Application/Features/KnowledgeBase/ContentTypeResolver.cs` — module root, alongside `KnowledgeBaseModule.cs` and `KnowledgeBaseOptions.cs`.
- (b) `Application/Features/KnowledgeBase/Services/ContentTypeResolver.cs` — under `Services/` with other domain services.
- (c) `Application/Features/KnowledgeBase/UseCases/IndexDocument/ContentTypeResolver.cs` — co-located with the primary consumer.
- (d) `Application/Shared/ContentTypeResolver.cs` — shared across all features.

**Chosen approach:** (a) — `Application/Features/KnowledgeBase/ContentTypeResolver.cs`.

**Rationale:**
- Matches the spec verbatim.
- (b) is for DI-registered services; this is a pure static helper.
- (c) couples the helper to one use case, but two use cases consume it; either folder becomes an arbitrary choice.
- (d) overreaches — the brief is scoped to KnowledgeBase. Promoting to `Shared/` would also need a Leaflet consolidation, which is out of scope. Keep it `internal` to `Anela.Heblo.Application` and at the module root, which still allows a future cross-module promotion without breaking consumers.

#### Decision 2: Visibility — `internal static` vs `public static`
**Options considered:** `internal` vs `public`.
**Chosen approach:** `internal static` (matches spec).
**Rationale:** Only callers are inside the `Anela.Heblo.Application` assembly. `internal` minimizes API surface, makes tests reside in the same assembly's test project (already the case — `Anela.Heblo.Tests` has `InternalsVisibleTo` access via standard project conventions, or tests can call via `internal` access if configured; verify the test project setup before finalizing — see *Prerequisites*).

#### Decision 3: Keep the double-call in the upload flow vs strip it
**Options considered:**
- (a) Both handlers call `Resolve` (spec FR-3); upload-flow resolution becomes idempotent (no-op the second time).
- (b) Only `UploadDocumentHandler` resolves; `IndexDocumentHandler` trusts pre-resolved input.

**Chosen approach:** (a).

**Rationale:** `IndexDocumentHandler` has two callers: `UploadDocumentHandler` (resolves first) and `KnowledgeBaseIngestionJob` (does not). Removing resolution from `IndexDocumentHandler` would silently break the ingestion-job path. Idempotency of `Resolve` makes the double-call free of cost, and centralizing the "I always have a resolved content type" invariant inside `IndexDocumentHandler` is defensive against any future third caller. The brief's suggestion to "drop the second call" is rejected for this reason — the spec correctly walks it back.

#### Decision 4: Test placement
**Options considered:**
- (a) `backend/test/Anela.Heblo.Tests/KnowledgeBase/ContentTypeResolverTests.cs` — at the module root, mirroring the source layout.
- (b) Under `UseCases/` — co-located with handler tests.

**Chosen approach:** (a). Mirrors the production layout (`Features/KnowledgeBase/ContentTypeResolver.cs` ↔ `KnowledgeBase/ContentTypeResolverTests.cs`).

## Implementation Guidance

### Directory / Module Structure

**New file:**
```
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/
└── ContentTypeResolver.cs                              ← NEW
```

**New test file:**
```
backend/test/Anela.Heblo.Tests/KnowledgeBase/
└── ContentTypeResolverTests.cs                         ← NEW
```

**Modified files:**
```
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/
├── UseCases/UploadDocument/UploadDocumentHandler.cs    ← delete lines 73-84, update call site at line 30
└── UseCases/IndexDocument/IndexDocumentHandler.cs      ← delete lines 141-152, update call site at line 30
```

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase;

internal static class ContentTypeResolver
{
    public static string Resolve(string contentType, string filename);
}
```

**Behavioral contract (must match current logic exactly):**

| Input `contentType`                            | `filename` extension | Returns                                                        |
|------------------------------------------------|----------------------|----------------------------------------------------------------|
| null or `""`                                   | `.pdf`               | `"application/pdf"`                                            |
| `"application/octet-stream"` (any case)        | `.docx`              | `"application/vnd.openxmlformats-officedocument...document"`  |
| `"application/octet-stream"`                   | `.doc`               | `"application/msword"`                                         |
| `"application/octet-stream"`                   | `.txt`               | `"text/plain"`                                                 |
| `"application/octet-stream"`                   | `.md`                | `"text/markdown"`                                              |
| `"application/octet-stream"`                   | `.xyz` (unknown)     | `"application/octet-stream"` (fallback — preserve input)       |
| null or `""`                                   | `.xyz` (unknown)     | `null` or `""` respectively (fallback returns input unchanged) |
| `"image/png"`                                  | any                  | `"image/png"` (non-octet pass-through)                         |
| `"APPLICATION/OCTET-STREAM"`                   | `.PDF`               | `"application/pdf"` (case-insensitive both sides)              |

**Idempotency property (must be unit-tested):**
```
Resolve(Resolve(ct, name), name) == Resolve(ct, name)   for all (ct, name)
```
This is the property that makes the double-call in the upload flow safe.

### Data Flow

**Upload flow (unchanged structure, helper extracted):**
1. `KnowledgeBaseController` receives multipart upload → dispatches `UploadDocumentRequest` via MediatR.
2. `UploadDocumentHandler.Handle`:
   - Copies stream to byte array.
   - **`contentType = ContentTypeResolver.Resolve(request.ContentType, request.Filename)`** (was: local private method).
   - Checks extractor support; returns `UnsupportedFileType` early if no extractor handles the resolved type.
   - Dispatches `IndexDocumentRequest` with resolved `ContentType`.
3. `IndexDocumentHandler.Handle`:
   - **`contentType = ContentTypeResolver.Resolve(request.ContentType, request.Filename)`** (idempotent no-op here since input is already resolved).
   - Continues with hashing, dedupe, persistence, indexing.

**Background ingestion flow (unchanged structure, helper extracted):**
1. `KnowledgeBaseIngestionJob` constructs `IndexDocumentRequest` directly (no upstream resolution).
2. `IndexDocumentHandler.Handle`:
   - **`contentType = ContentTypeResolver.Resolve(request.ContentType, request.Filename)`** (real work happens here).
   - Continues as above.

### Test Strategy

Single new test class `ContentTypeResolverTests` covering:

| Test name (suggested)                                                | Inputs                                                | Asserts                          |
|----------------------------------------------------------------------|-------------------------------------------------------|----------------------------------|
| `Resolve_OctetStream_Pdf_ReturnsApplicationPdf`                      | `("application/octet-stream", "x.pdf")`               | `"application/pdf"`              |
| `Resolve_OctetStream_Docx_ReturnsWordprocessing`                     | `("application/octet-stream", "x.docx")`              | full docx MIME                   |
| `Resolve_OctetStream_Doc_ReturnsMsword`                              | `("application/octet-stream", "x.doc")`               | `"application/msword"`           |
| `Resolve_OctetStream_Txt_ReturnsTextPlain`                           | `("application/octet-stream", "x.txt")`               | `"text/plain"`                   |
| `Resolve_OctetStream_Md_ReturnsTextMarkdown`                         | `("application/octet-stream", "x.md")`                | `"text/markdown"`                |
| `Resolve_EmptyContentType_Pdf_ReturnsApplicationPdf`                 | `("", "x.pdf")`                                       | `"application/pdf"`              |
| `Resolve_NullContentType_Pdf_ReturnsApplicationPdf`                  | `(null!, "x.pdf")`                                    | `"application/pdf"`              |
| `Resolve_OctetStream_UnknownExtension_ReturnsOriginalOctetStream`    | `("application/octet-stream", "x.xyz")`               | `"application/octet-stream"`     |
| `Resolve_NonOctetStream_PassesThrough_RegardlessOfExtension`         | `("image/png", "x.pdf")`                              | `"image/png"`                    |
| `Resolve_CaseInsensitive_OctetStream_StillResolves`                  | `("APPLICATION/OCTET-STREAM", "x.PDF")`               | `"application/pdf"`              |
| `Resolve_IsIdempotent`                                               | parametrized over all rows above                      | `Resolve(Resolve(x,n),n)==Resolve(x,n)` |

Prefer `[Theory]`/`[InlineData]` for the mapping table; one `[Fact]` for the idempotency property. xUnit + FluentAssertions, consistent with `UploadDocumentHandlerTests` and other tests in the same project.

The existing `UploadDocumentHandlerTests` and `IndexDocumentHandlerTests` should require **no behavioral changes** — only mechanical updates if they reference the deleted private methods (they should not, since those methods were private).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Test project cannot access `internal` members of `Application` assembly | Low | Verify `InternalsVisibleTo("Anela.Heblo.Tests")` exists in the `Application` `.csproj` or via assembly attribute. If absent, add it. (See *Prerequisites*.) |
| Subtle copy-paste error during deletion changes call-site behavior | Low | Tests for both handlers already assert on `ContentType` flowing through correctly. The new resolver tests pin the contract. Mitigation is simply: don't rename arguments or reorder them. |
| Future contributor restores a local `ResolveContentType` in a handler | Low | A code review with the `csharp-reviewer` agent will catch this. Optional: add a banned-symbol analyzer rule, but this is overengineering for the change. |
| Leaflet handler retains its identical copy → silent divergence between modules | Low (out of scope) | Document in *Specification Amendments*. Open a separate brief if desired. |
| Resolver signature accepts `string` for `contentType` but real callers may pass `null` | Low | The current implementation already calls `string.IsNullOrEmpty(contentType)` and short-circuits. Behavior preserved. Nullable annotations on the new method should follow the project's existing handler conventions (`string contentType` non-nullable — matches `IndexDocumentRequest.ContentType` default-empty). |
| `internal` visibility blocks future cross-feature reuse (e.g. Leaflet) | Low | `internal` still permits use anywhere in `Anela.Heblo.Application`. Both Leaflet and KnowledgeBase live in that assembly, so a future consolidation is a visibility no-op. |

## Specification Amendments

The spec is sound. Three small additions strengthen it:

1. **Add an idempotency unit-test requirement** to FR-3 acceptance criteria (already mentioned in prose, but list as an explicit acceptance bullet):
   > A unit test verifies `Resolve(Resolve(ct, name), name) == Resolve(ct, name)` for the supported mapping table and at least one pass-through case.

2. **Document the existing duplicate in `Leaflet/UploadLeaflet/UploadLeafletHandler.cs` (lines 73–84) as a known follow-up.** Add to *Out of Scope*:
   > A fourth identical copy of `ResolveContentType` exists in `Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs`. Consolidating it (and any future cross-module promotion of `ContentTypeResolver` to `Application/Shared/`) is explicitly out of scope for this refactor.

3. **Pin the resolver's `contentType` parameter nullability.** The current handler private methods accept `string` (non-nullable). To preserve call-site signatures byte-for-byte, the public method on `ContentTypeResolver` must also be `string contentType` (non-nullable), with `string.IsNullOrEmpty` handling the empty case. Add to FR-1 acceptance criteria:
   > Method signature: `public static string Resolve(string contentType, string filename)` — both parameters non-nullable; behavior on empty `contentType` preserved by internal `IsNullOrEmpty` check.

No other amendments. The brief's suggestion to "drop the second resolution call in `IndexDocumentHandler`" is correctly walked back by FR-3 of the spec.

## Prerequisites

1. **Confirm `InternalsVisibleTo` for the test assembly.** The new type is `internal`; `Anela.Heblo.Tests` must be able to see it. Before writing the test class, check `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` for `<InternalsVisibleTo Include="Anela.Heblo.Tests" />` (or equivalent `[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]` in an `AssemblyInfo.cs`). If missing, add it as part of this change. If the team prefers not to widen the visibility seam, the fallback is `public static` — but that needlessly grows the public API surface.
2. **No DI changes.** `KnowledgeBaseModule.cs` is not touched.
3. **No migrations, no config changes, no Key Vault entries, no Azure changes.**
4. **No frontend changes, no OpenAPI regeneration.**
5. **Validation gate** before declaring complete (per `CLAUDE.md`):
   - `dotnet build` clean.
   - `dotnet format` clean.
   - All KnowledgeBase tests pass: `dotnet test --filter "FullyQualifiedName~KnowledgeBase"`.
   - At minimum, the new `ContentTypeResolverTests`, the existing `UploadDocumentHandlerTests`, and `IndexDocumentHandlerTests` are green.
```