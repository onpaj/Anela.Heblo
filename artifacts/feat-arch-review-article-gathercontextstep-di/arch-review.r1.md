# Architecture Review: Decouple Article Module from KnowledgeBase via `IArticleStyleGuideSource`

## Skip Design: true

## Architectural Fit Assessment

The proposal aligns directly with the codebase's documented cross-module communication pattern (`docs/architecture/development_guidelines.md` §Cross-Module Communication Example). It is a literal application of the `ILeafletKnowledgeSource` precedent already implemented and validated by `ModuleBoundariesTests.cs`. No new patterns are introduced; this is a "second instance of an existing pattern" refactor.

Integration points:
- **Consumer-owned contract** in `Application/Features/Article/Contracts/` (new folder for Article — it does not exist yet; only `UseCases/` lives under Article today).
- **Provider adapter** in `Application/Features/KnowledgeBase/Infrastructure/`, sitting next to `KnowledgeBaseLeafletSourceAdapter.cs`.
- **DI binding** added inside `KnowledgeBaseModule.AddKnowledgeBaseModule`, next to the existing `services.AddScoped<ILeafletKnowledgeSource, KnowledgeBaseLeafletSourceAdapter>();` line.
- **Architecture test** extended via a new `ModuleBoundaryRule` entry in `Rules()` TheoryData — the existing `[Theory]/[MemberData]` infrastructure handles this without code changes.

One issue requires resolution before implementation: the spec scope addresses only `IOneDriveService`, but `GatherContextStep` also imports `Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments` (uses `SearchDocumentsRequest` via MediatR at line 89). A new module-boundary rule with no allowlist will fail. See *Specification Amendments* below.

## Proposed Architecture

### Component Overview

```
Article module                            KnowledgeBase module
─────────────────────────                  ─────────────────────────────────────
Contracts/                                 Infrastructure/
  IArticleStyleGuideSource  ◄────── impl ─ KnowledgeBaseArticleStyleGuideSource
       │                                     │
       │ injected by                         │ delegates to
       ▼                                     ▼
UseCases/Generate/Pipeline/                Services/
  GatherContextStep                        IOneDriveService
                                              (existing, unchanged)

KnowledgeBaseModule.cs (DI composition root)
  services.AddScoped<IArticleStyleGuideSource, KnowledgeBaseArticleStyleGuideSource>();

ModuleBoundariesTests.cs
  ModuleBoundaryRule "Article -> KnowledgeBase" (new TheoryData entry, with allowlist
  for SearchDocumentsRequest until that consumption is also lifted behind a contract).
```

### Key Design Decisions

#### Decision 1: Adapter naming — `KnowledgeBaseArticleStyleGuideSource`
**Options considered:**
- A. `OneDriveArticleStyleGuideSource` (spec proposal — leads with backing service)
- B. `KnowledgeBaseArticleStyleGuideSource` (matches existing `KnowledgeBaseLeafletSourceAdapter`)

**Chosen approach:** B.

**Rationale:** Consistency with `KnowledgeBaseLeafletSourceAdapter`. The adapter is owned by the KnowledgeBase module and its name should advertise *which provider* is satisfying the consumer contract, not *which infrastructure detail* it happens to use today (OneDrive could be replaced internally without renaming the adapter).

#### Decision 2: Where to place the architecture rule
**Options considered:**
- A. New `[Fact]` test method, mirroring `Logistics_types_should_not_reference_Purchase_owned_namespaces`.
- B. New entry in the existing `Rules()` TheoryData, reused by the parameterised `Consumer_types_should_not_reference_provider_owned_namespaces` theory.

**Chosen approach:** B.

**Rationale:** The existing theory already supports per-rule allowlists, multiple forbidden namespace prefixes, and full reflection traversal. Adding a `ModuleBoundaryRule` row is one TheoryData entry plus an allowlist `HashSet`. Code duplication (Option A) is unwarranted.

#### Decision 3: Adapter visibility — `internal sealed`
**Options considered:** `public` vs `internal sealed`.

**Chosen approach:** `internal sealed` (mirroring `KnowledgeBaseLeafletSourceAdapter`).

**Rationale:** The adapter is implementation detail of the KnowledgeBase module. Only DI registration needs to see it. `sealed` prevents accidental subclassing of an adapter that should not have behavior of its own.

#### Decision 4: Method signature — preserve `driveId`/`path` parameters verbatim
**Options considered:**
- A. Pass `driveId` and `path` (spec proposal).
- B. Lift to a higher-level `LoadAsync(Article article)` that hides the OneDrive shape.

**Chosen approach:** A.

**Rationale:** The Article domain entity already owns these as first-class properties (`StyleGuideDriveId`, `StyleGuideItemPath`). The contract is for a *style-guide source*, not for *style-guide resolution*. Article remains responsible for "which style guide" and the contract for "fetch the bytes". Option B would couple the adapter to the Article domain entity, creating a backwards module reference.

#### Decision 5: Architecture rule scope and allowlist
**Options considered:**
- A. Add the rule with an empty allowlist and block the spec scope from expanding (test would fail because of `SearchDocumentsRequest`).
- B. Add the rule with an allowlist entry for the surviving `SearchDocumentsRequest`/`SearchDocumentsResponse`/`ChunkResult` references, mirroring the pre-existing `LeafletAllowlist` precedent. Track follow-up to lift the search query behind another consumer-owned contract.
- C. Expand spec scope to also lift `SearchDocumentsRequest` now.

**Chosen approach:** B.

**Rationale:** The spec explicitly forbids silent scope expansion ("Out of Scope" §"Auditing other Article-module files…"). Allowlist precedent already exists in the same test class for exactly this situation (`LeafletAllowlist` carries entries for `IDocumentTextExtractor` and `IOneDriveService` with TODO comments). Adding the rule with an empty allowlist (Option A) would make CI red on day one. Lifting `SearchDocumentsRequest` now (Option C) violates the spec's explicit scope.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/
├── Article/
│   ├── Contracts/                                      ← NEW folder
│   │   └── IArticleStyleGuideSource.cs                 ← NEW
│   ├── ArticleModule.cs                                (unchanged)
│   └── UseCases/Generate/Pipeline/
│       └── GatherContextStep.cs                        ← MODIFIED
└── KnowledgeBase/
    ├── Infrastructure/
    │   ├── KnowledgeBaseLeafletSourceAdapter.cs        (precedent — reference)
    │   └── KnowledgeBaseArticleStyleGuideSource.cs     ← NEW
    └── KnowledgeBaseModule.cs                          ← MODIFIED (one line)

backend/test/Anela.Heblo.Tests/
├── Architecture/
│   └── ModuleBoundariesTests.cs                        ← MODIFIED (new Rule + allowlist)
└── Article/Pipeline/
    └── GatherContextStepTests.cs                       ← MODIFIED (mock type swap)
```

### Interfaces and Contracts

```csharp
// Article/Contracts/IArticleStyleGuideSource.cs
namespace Anela.Heblo.Application.Features.Article.Contracts;

/// <summary>
/// Retrieves the Article module's style guide text from an external source.
/// Implemented by the KnowledgeBase module via an adapter.
/// </summary>
public interface IArticleStyleGuideSource
{
    Task<string> DownloadStyleGuideTextAsync(
        string driveId,
        string path,
        CancellationToken cancellationToken);
}
```

```csharp
// KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSource.cs
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;

internal sealed class KnowledgeBaseArticleStyleGuideSource : IArticleStyleGuideSource
{
    private readonly IOneDriveService _oneDrive;

    public KnowledgeBaseArticleStyleGuideSource(IOneDriveService oneDrive)
    {
        _oneDrive = oneDrive;
    }

    public Task<string> DownloadStyleGuideTextAsync(
        string driveId,
        string path,
        CancellationToken cancellationToken) =>
        _oneDrive.DownloadFileTextByPathAsync(driveId, path, cancellationToken);
}
```

DI registration (inside `KnowledgeBaseModule.AddKnowledgeBaseModule`, next to the existing Leaflet binding):

```csharp
services.AddScoped<IArticleStyleGuideSource, KnowledgeBaseArticleStyleGuideSource>();
```

Lifetime: `Scoped`, matching `ILeafletKnowledgeSource`. The adapter holds no state; it inherits the lifetime of the underlying `IOneDriveService` (also `Scoped`).

### Data Flow

```
ArticlePipelineContext (Article domain Article with StyleGuideDriveId, StyleGuideItemPath)
  → GatherContextStep.LoadStyleGuideAsync
  → IArticleStyleGuideSource.DownloadStyleGuideTextAsync(driveId, path, ct)
  → KnowledgeBaseArticleStyleGuideSource (adapter, KnowledgeBase-owned)
  → IOneDriveService.DownloadFileTextByPathAsync(driveId, path, ct)
  → GraphOneDriveService | MockOneDriveService (selected by KnowledgeBaseModule based on
    SharePoint config + auth mode — unchanged)
  → string returned all the way back to context.StyleGuideText
```

Error/cancellation propagation: the adapter does not catch. Existing `try/catch (Exception ex) when (ex is not OperationCanceledException)` block in `GatherContextStep.LoadStyleGuideAsync` continues to log warnings and return `null` for non-cancellation exceptions. Cancellation surfaces as `OperationCanceledException` unchanged.

### Test refactor

`GatherContextStepTests.cs` currently has `Mock<IOneDriveService> _oneDrive = new();` and constructs the step with `_oneDrive.Object`. After the refactor:
- Replace with `Mock<IArticleStyleGuideSource> _styleGuideSource = new();`.
- Remove `using Anela.Heblo.Application.Features.KnowledgeBase.Services;` (the file still imports `KnowledgeBase.UseCases.SearchDocuments` to type the `SearchDocumentsRequest` MediatR mock — that mirrors the spec's accepted out-of-scope item and is allowed in test code, which is not under the architecture rule).
- Existing tests do not currently exercise the style guide path. Consider adding a minimal happy-path test for `LoadStyleGuideAsync` to lock the wiring through the new contract.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| New `ModuleBoundaryRule` for Article → KnowledgeBase fails because `GatherContextStep` still references `SearchDocumentsRequest` / `SearchDocumentsResponse` / `ChunkResult` via MediatR. | **High** — would break CI immediately. | Add allowlist entries for these three types in a new `ArticleAllowlist`, with an inline comment explaining the deferral and linking to the follow-up. Pattern is established by `LeafletAllowlist`. |
| Adapter naming drift between spec (`OneDriveArticleStyleGuideSource`) and Leaflet precedent (`KnowledgeBaseLeafletSourceAdapter`). | Low | Use `KnowledgeBaseArticleStyleGuideSource` for consistency. |
| Implementer puts `IArticleStyleGuideSource` in `Article/Services/` or similar, breaking the documented `Contracts/` ownership rule. | Medium | Spec is explicit: `Application/Features/Article/Contracts/IArticleStyleGuideSource.cs`. New `Contracts/` folder must be created — Article does not have one yet. |
| Reflection-based architecture test passes locally but fails under different compile flags (e.g., trimming, async state machines). | Low | The existing helper `EnumerateReferencedTypes` already inspects async state machines indirectly via field types and the test handles compiler-generated nested types through the `DeclaringType` check (see ModuleBoundariesTests.cs:152-159). |
| Future regression where a new Article use case re-imports a KnowledgeBase namespace. | Low (by design) | The new architecture rule is the regression guard. CI will block the PR. |

## Specification Amendments

1. **FR-5 must include an allowlist for surviving `SearchDocumentsRequest` references.**
   `GatherContextStep` calls `_mediator.Send(new SearchDocumentsRequest { ... }, ct)` at line 89 and binds `response.Chunks` (`ChunkResult`) at lines 92–99. These types live in `Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments` and will be flagged by the new rule. Per the spec's own "Out of Scope" statement, do not lift them now — add allowlist entries with a TODO comment, mirroring `LeafletAllowlist` (ModuleBoundariesTests.cs:29-45). Required entries:
   - `Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline.GatherContextStep -> Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments.SearchDocumentsRequest`
   - `Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline.GatherContextStep -> Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments.SearchDocumentsResponse`
   - `Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline.GatherContextStep -> Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments.ChunkResult`

2. **Adapter name: `KnowledgeBaseArticleStyleGuideSource`** (not `OneDriveArticleStyleGuideSource`) for consistency with `KnowledgeBaseLeafletSourceAdapter`.

3. **Acceptance for FR-5 should add:** "An `ArticleAllowlist : HashSet<string>` constant is added next to the existing per-module allowlists, each entry annotated with a comment explaining the deferred decoupling work."

4. **FR-4 should explicitly note** that `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;` *remains* in `GatherContextStep.cs` for `SearchDocumentsRequest` until the follow-up. Only the `using Anela.Heblo.Application.Features.KnowledgeBase.Services;` line is removed. This clarifies the "no `using Anela.Heblo.Application.Features.KnowledgeBase...` line" criterion which is currently overstated.

5. **Open the follow-up explicitly.** File a tracking item: "Lift `SearchDocumentsRequest` behind a consumer-owned `IArticleKnowledgeSearch` contract; remove `ArticleAllowlist` entries." Spec currently buries this in "Out of Scope" without an artifact.

## Prerequisites

None. All required infrastructure exists:
- `Contracts/` folder convention is documented and used by `Leaflet/Contracts/`.
- `KnowledgeBase/Infrastructure/` adapter folder exists and already houses one adapter.
- `KnowledgeBaseModule.AddKnowledgeBaseModule` already registers one cross-module contract.
- `ModuleBoundariesTests` supports adding new rules via `Rules()` TheoryData with no test infrastructure changes.
- `IOneDriveService` DI registration (mock vs Graph based on environment) is unchanged and automatically inherited by the new adapter.
- No NuGet packages, migrations, configuration, or environment variables required.