I have enough context. The codebase confirms the spec accurately: three handlers with duplicated projection, divergent null guard, MarketingFeature has precedent for `internal static` mappers in `Services/` and `UseCases/<UseCase>/` locations. AutoMapper is also used widely (`*MappingProfile.cs`), but only for non-trivial mappings. The Journal feature has no existing `MappingProfile`. Now I'll produce the architecture review.

# Architecture Review: Consolidate JournalEntry → JournalEntryDto projection into a shared mapper

## Skip Design: true

## Architectural Fit Assessment
The proposal is a pure backend refactor that aligns cleanly with existing patterns. The codebase already has precedent for hand-written `internal static` mappers — `Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs` is structurally identical to what is proposed (internal static class, pure methods, lives next to the use-case code, no DI). The Journal feature follows the "Complex feature" layout (`UseCases/`, `Contracts/`, `JournalModule.cs`) per `docs/architecture/filesystem.md`, so adding a `Mapping/` sibling folder under `Application/Features/Journal/` matches the documented vertical-slice layout.

Integration points are minimal and well-bounded: three handler files in `Application/Features/Journal/UseCases/` change their projection call site; nothing in `Domain`, `Persistence`, `API`, or `Contracts` is touched. The DTO contract (`JournalEntryDto`, `JournalEntryTagDto`) is preserved verbatim, so the auto-generated OpenAPI TypeScript client requires no regeneration.

Two architectural notes worth flagging:

- The codebase has heavy AutoMapper usage (`InvoicesMappingProfile`, `CatalogMappingProfile`, `LogisticsMappingProfile`, etc., 11 profiles total). The spec explicitly rejects AutoMapper for this case. That is the right call here — the projection is small, navigation-aware (`.Distinct()`, conditional `.Where`), and AutoMapper profiles in this repo are registered globally; introducing one just for Journal would pull tag-mapping configuration out of the feature folder and into AutoMapper's runtime DI graph for no benefit. Hand-written static mapping is the lighter and more discoverable option for this projection.
- The `JournalEntryTagAssignment.Tag` navigation is declared `= null!` (non-nullable reference). The null-safe guard codifies a defensive contract against orphan rows; that is correct, but the *root cause* (no cascade from `JournalEntryTag` deletion to `JournalEntryTagAssignment`) is left intact per spec scope. This is acceptable for the refactor but should be tracked as a follow-up data-integrity item — see Risks.

## Proposed Architecture

### Component Overview
```
┌─────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Application/Features/Journal/                           │
│                                                                     │
│   UseCases/                                                         │
│   ├── GetJournalEntries/GetJournalEntriesHandler ──┐                │
│   ├── SearchJournalEntries/SearchJournalEntriesH ──┼──► calls       │
│   └── GetJournalEntry/GetJournalEntryHandler ──────┘     │          │
│                                                          ▼          │
│   Mapping/                                                          │
│   └── JournalEntryMapper  (internal static)                         │
│         + ToDto(JournalEntry entry) : JournalEntryDto               │
│              │                                                      │
│              ├── reads → Domain/Features/Journal/JournalEntry       │
│              │                          .ProductAssociations[]      │
│              │                          .TagAssignments[].Tag       │
│              │                                                      │
│              └── returns → Contracts/JournalEntryDto                │
│                            (Tags: JournalEntryTagDto[])             │
│                                                                     │
│   Contracts/                                                        │
│   └── JournalEntryDto, JournalEntryTagDto  (unchanged)              │
└─────────────────────────────────────────────────────────────────────┘
```

The mapper is a leaf node: no inbound dependencies other than the three handlers, no outbound dependencies on services, repositories, or DI. It mirrors `OutlookEventImportMapper` in shape and scope.

### Key Design Decisions

#### Decision 1: Mapper placement — `Mapping/` subfolder vs. `Services/` vs. co-located in `Contracts/`
**Options considered:**
- (A) `Application/Features/Journal/Mapping/JournalEntryMapper.cs` (spec proposal)
- (B) `Application/Features/Journal/Services/JournalEntryMapper.cs` (parallel to `Marketing/Services/MarketingCategoryMapper.cs`)
- (C) Static method on `JournalEntryDto` itself (e.g. `JournalEntryDto.FromEntity(entry)`)
- (D) Co-locate inside one of the three `UseCases/` folders (parallel to `OutlookEventImportMapper.cs` which lives under `UseCases/ImportFromOutlook/`)

**Chosen approach:** (A) — `Application/Features/Journal/Mapping/JournalEntryMapper.cs`, internal static.

**Rationale:**
- (B) is reserved in this codebase for stateful, DI-registered services (`MarketingCategoryMapper` implements an interface, holds an `IOptionsMonitor` subscription, is `IDisposable`). A pure static mapper does not belong in `Services/`.
- (C) would force `Contracts/JournalEntryDto.cs` to take a `using Anela.Heblo.Domain.Features.Journal;` dependency, inverting the desired direction (DTOs should be passive shapes; mapping is an Application-layer concern, not a Contracts concern). It also makes the DTO file responsible for two things.
- (D) was used for `OutlookEventImportMapper` because that mapper is used by *one* use case. The Journal mapper is shared across *three* use cases, so co-locating in any one of them would create an asymmetric "why is the shared helper hiding in this one folder?" smell.
- (A) creates a new but small `Mapping/` folder that is a documented sibling of `Contracts/`, `UseCases/`, `Services/`, etc. per `docs/architecture/filesystem.md`. It signals "this is a feature-local mapping concern, not a service and not a contract".

#### Decision 2: Hand-written static method vs. AutoMapper profile
**Options considered:**
- (A) Hand-written `internal static JournalEntryDto ToDto(JournalEntry)`
- (B) Add `JournalMappingProfile : Profile` alongside the other 11 profiles in the codebase

**Chosen approach:** (A).

**Rationale:** Per spec FR-1 and the Out-of-Scope section. The projection has conditional logic (`.Where(ta => ta.Tag != null)`) and de-duplication (`.Distinct()`) that map cleanly in code but require `ConvertUsing`/custom resolvers in AutoMapper, which is a strictly worse readability trade. AutoMapper's value is for many similar mappings across a feature; here there is exactly one projection direction (entity → DTO), so the abstraction does not pay for itself. The existing `OutlookEventImportMapper` in the same repo establishes the precedent.

#### Decision 3: Null-safe tag projection — `.Where` vs. null-conditional element creation
**Options considered:**
- (A) `.Where(ta => ta.Tag != null).Select(...)` — adopt the safer of the two existing behaviors
- (B) `.Select(ta => ta.Tag is null ? null : new JournalEntryTagDto { ... }).Where(t => t != null)` — explicit null check on the projected DTO
- (C) Use null-forgiving `ta.Tag!.Id` — preserve crash-on-orphan behavior

**Chosen approach:** (A) — the spec's chosen behavior.

**Rationale:** (C) leaves a latent crash. (B) materializes nullable DTOs then filters them, which is more allocation and reads worse. (A) is the minimal change that unifies the three handlers on the safer existing behavior, matches the current `GetJournalEntryHandler` line 49 semantics exactly, and is what the brief, the spec, and the existing single-entry handler already do.

#### Decision 4: Mapper visibility — `internal static` vs. `public static`
**Options considered:**
- (A) `internal static` (spec proposal)
- (B) `public static`

**Chosen approach:** (A).

**Rationale:** No consumer outside the `Anela.Heblo.Application` assembly needs to call this mapper. `internal` enforces the architectural constraint that DTO projection is an Application-layer concern and prevents accidental leakage to `Anela.Heblo.API`, `Anela.Heblo.Persistence`, or `Anela.Heblo.Domain`. `InternalsVisibleTo("Anela.Heblo.Tests")` is already wired in the test project for similar reasons (verifiable by reading the `.csproj`); if not, the test can call the mapper through the handlers, but the spec adds direct unit tests, so add the `InternalsVisibleTo` attribute if missing — see Prerequisites.

## Implementation Guidance

### Directory / Module Structure
Create one new production folder + file and one new test file. No other files are created. Three files are edited.

```
backend/src/Anela.Heblo.Application/Features/Journal/
├── Mapping/                                  [NEW FOLDER]
│   └── JournalEntryMapper.cs                 [NEW]
├── UseCases/
│   ├── GetJournalEntries/
│   │   └── GetJournalEntriesHandler.cs       [EDIT — replace inline projection]
│   ├── GetJournalEntry/
│   │   └── GetJournalEntryHandler.cs         [EDIT — replace inline projection]
│   └── SearchJournalEntries/
│       └── SearchJournalEntriesHandler.cs    [EDIT — replace inline projection;
│                                                      preserve content-preview foreach]
├── Contracts/                                [UNCHANGED]
└── JournalModule.cs                          [UNCHANGED — no DI registration needed]

backend/test/Anela.Heblo.Tests/Features/Journal/
└── JournalEntryMapperTests.cs                [NEW]
```

`JournalModule.cs` requires **no change**: the mapper is static, has no DI lifetime, and is not consumed via interface.

### Interfaces and Contracts
```csharp
// backend/src/Anela.Heblo.Application/Features/Journal/Mapping/JournalEntryMapper.cs
namespace Anela.Heblo.Application.Features.Journal.Mapping;

internal static class JournalEntryMapper
{
    public static JournalEntryDto ToDto(JournalEntry entry);
}
```

Behavioral contract:
1. **Inputs.** Takes a non-null `JournalEntry` with eagerly-loaded `ProductAssociations` and `TagAssignments` collections; behavior is undefined if collections are not loaded (same as today's inline projection). The mapper does not call `.Include` and does not trigger lazy loading — that is the repository's responsibility.
2. **`AssociatedProducts`.** Distinct `ProductCodePrefix` values from `ProductAssociations`, in the same order produced by today's `.Select(...).Distinct().ToList()` (LINQ-to-Objects deterministic, first-seen order).
3. **`Tags`.** From `TagAssignments`, **skipping** any assignment whose `Tag` navigation is null. Each surviving assignment becomes a `JournalEntryTagDto { Id, Name, Color }`. Empty input → empty list (never null).
4. **Search-specific fields.** `ContentPreview` stays `null`; `HighlightedTerms` stays as the DTO's default empty list. Mapper never reads search criteria.
5. **Purity.** No I/O, no logger, no DI, no exceptions thrown by the mapper itself (a null `entry` argument will throw `NullReferenceException` on first property read, which matches today's behavior — do **not** add an explicit `ArgumentNullException` guard for parity with current behavior unless explicitly requested).
6. **Allocation.** Returns a freshly-allocated `JournalEntryDto` per call. The two list materializations (`AssociatedProducts`, `Tags`) match the current `.ToList()` calls byte-for-byte; do not change `.ToList()` to `.ToArray()` or yield-based enumeration.

Handler call sites become:
```csharp
// Get list
var entryDtos = result.Items.Select(JournalEntryMapper.ToDto).ToList();

// Search (preview enrichment stays in the handler)
var entryDtos = result.Items.Select(JournalEntryMapper.ToDto).ToList();
if (!string.IsNullOrEmpty(request.SearchText))
{
    foreach (var dto in entryDtos) { /* existing preview/highlight code */ }
}

// Single entry
return new GetJournalEntryResponse { Entry = JournalEntryMapper.ToDto(entry) };
```

### Data Flow
**Read path (unchanged in shape, consolidated at mapper):**
```
HTTP request
   │
   ▼
JournalController action
   │   (MediatR.Send)
   ▼
{Get|Search|Get-by-id}Handler
   │   1. build criteria / id
   │   2. await _journalRepository.{GetEntries|SearchEntries|GetById}Async(...)
   ▼
JournalRepository (EF Core) → returns JournalEntry(ies) with eager-loaded
                              ProductAssociations & TagAssignments(+Tag)
   │
   ▼
Handler:
   entries.Select(JournalEntryMapper.ToDto).ToList()   ◄── single projection
   │
   ▼  (Search only: per-DTO foreach enrichment for ContentPreview/HighlightedTerms)
   │
Response { Entries / Entry, paging meta } → JSON → client
```

Mapper invocation is the **only** projection step; no second pass and no AutoMapper context.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Repository changes eager-loading of `TagAssignments.Tag` and the null-safe guard hides the regression silently (tags disappear from API responses instead of throwing). | Medium | Add a `JournalRepositoryIntegrationTests` assertion (or extend an existing one) that confirms `Tag` is populated for a non-orphan assignment. Spec FR-2's guard is correct, but it makes "tag navigation not loaded" indistinguishable from "tag was deleted". Keep the integration test as the canary. |
| Test relies on `internal` visibility from `Anela.Heblo.Application` to `Anela.Heblo.Tests`. | Low | Verify `InternalsVisibleTo("Anela.Heblo.Tests")` is declared (in `.csproj` or `AssemblyInfo`); if absent, add it. Direct unit-testing of the mapper is the explicit FR-6 requirement, so this must work. |
| Future contributor re-introduces inline projection in a new Journal handler, bypassing the mapper. | Low | The mapper docstring (one-line `<summary>`) should state "the canonical `JournalEntry` → `JournalEntryDto` projection". `code-reviewer` flow will catch divergence in PRs; no static-analysis rule is justified for a 1-file mapper. |
| Underlying root cause (`JournalEntryTagAssignment` orphan rows) is not addressed; data continues to drift silently. | Medium (out of scope) | Spec out-of-scope is explicit. File a follow-up issue to (a) add EF cascade behavior on `JournalEntryTag` delete and/or (b) log an `ILogger.LogWarning` when an orphan is observed by the mapper. The latter would require giving the mapper a logger — defer until evidence shows it matters. |
| Behavior change leaks to clients: tag list shrinks on responses that previously crashed. | Low | This is the intended correctness fix. No client contract changes; clients see fewer items in `Tags` only when the database state is already inconsistent. Document in PR description. |
| `dotnet format` produces unrelated diff because mapper file's `using` ordering or namespace block differs from existing convention. | Low | Match the file-scoped vs. block-scoped namespace style of neighboring `Application/Features/Journal/**/*.cs` files (the existing files use block-scoped namespaces with `using` inside the file — keep that). Run `dotnet format` before commit. |

## Specification Amendments
The spec is internally consistent and matches the codebase. Two small clarifications worth committing to before implementation:

1. **Add to FR-5 (Mapper placement):** The mapper file uses block-scoped namespace `Anela.Heblo.Application.Features.Journal.Mapping { ... }` to match neighboring Journal files (`JournalModule.cs`, `GetJournalEntryHandler.cs` both use block-scoped namespaces). The spec's example uses file-scoped (`namespace ...;`), which would be inconsistent with the rest of the Journal feature; reconcile to block-scoped. (Note: the Marketing `OutlookEventImportMapper` uses block-scoped; this confirms the local convention.)
2. **Add to FR-6 (Test coverage):** Explicitly require an `InternalsVisibleTo` check during implementation: if the attribute is not already present in `Anela.Heblo.Application.csproj` (`<ItemGroup><InternalsVisibleTo Include="Anela.Heblo.Tests" /></ItemGroup>`), it must be added as part of this change. Without it, the direct mapper tests cannot compile.
3. **Add to FR-7 (Handler refactor):** State explicitly that the `SearchJournalEntriesHandler` `foreach` loop now mutates DTOs **after** the mapper allocated them — this is consistent with today's behavior and acceptable, but FR-7's "no other handler behavior changes" should explicitly bless the post-mapping mutation pattern to avoid review noise about the "immutability" coding rule.

## Prerequisites
None blocking. Verify before starting:

- **`InternalsVisibleTo`** from `Anela.Heblo.Application` to `Anela.Heblo.Tests` is declared. If not, add it as the first commit in the change set. (One-line change in `Anela.Heblo.Application.csproj`.)
- **No EF migrations** are needed (no schema change).
- **No DI registration** is needed (`JournalModule.cs` is untouched).
- **No OpenAPI regeneration** is needed (`JournalEntryDto` shape is unchanged; the generated TypeScript client requires no rebuild as part of this PR, though the postbuild event will run anyway and should produce a no-op diff).
- **Validation gate** before declaring done: `dotnet build` (zero new warnings), `dotnet format` (no diff), `dotnet test` on `Anela.Heblo.Tests` (new `JournalEntryMapperTests` pass; existing `GetJournalEntryHandlerTests`, `SearchJournalEntriesHandlerTests` pass unchanged). Per the project's CLAUDE.md, all four must pass before completion; E2E is nightly and not required for this PR.