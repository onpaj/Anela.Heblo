# Specification: Relocate `PagedResult<T>` to the Xcc Shared Layer

## Summary
`PagedResult<T>` is currently declared inside `IJournalRepository.cs` in the `Anela.Heblo.Domain.Features.Journal` namespace, even though it is a generic, cross-cutting pagination utility with no Journal-specific business meaning. The Marketing module imports it directly from Journal's domain namespace, creating a forbidden cross-module dependency. This change moves `PagedResult<T>` to the shared `Anela.Heblo.Xcc.Persistance` namespace (alongside `IRepository<T, TKey>`) so both Journal and Marketing — and any future module — consume it from the shared layer instead of from another feature module.

## Background
The project follows Vertical Slice / Clean Architecture organization. Module boundaries are enforced at the domain layer: a feature module must not reach into another feature module's domain namespace for types. Generic, infrastructure-flavored types (pagination, repository abstractions, base entities) belong in the cross-cutting `Anela.Heblo.Xcc` project.

Today the dependency graph contains the following violation:

- `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs:26-32` declares `public class PagedResult<T>` inside namespace `Anela.Heblo.Domain.Features.Journal`.
- `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs:5,14` imports `Anela.Heblo.Domain.Features.Journal` solely to reference `PagedResult<MarketingAction>`.

Risks of the current placement:
1. **Hidden cross-module coupling.** The type name gives no hint that it lives in another feature's domain. A future rename, extraction, or removal of the Journal namespace silently breaks Marketing (and any other downstream consumer).
2. **Guideline breach.** `docs/architecture/development_guidelines.md` forbids direct access to another module's domain types.
3. **Discoverability friction.** Future modules that need paginated repository methods will either re-declare a near-duplicate `PagedResult<T>` or replicate the same illegal import.

A search of the backend shows that `PagedResult<T>` is referenced in five files only (Journal repo interface + impl, Marketing repo interface + impl, and one Journal test), so the blast radius of the move is small and fully containable in this change.

## Functional Requirements

### FR-1: Introduce `PagedResult<T>` in the Xcc shared layer
Add a new file `backend/src/Anela.Heblo.Xcc/Persistance/PagedResult.cs` containing a `PagedResult<T>` class in namespace `Anela.Heblo.Xcc.Persistance`. The shape must be byte-compatible with the current definition so that no consumer needs property renames:

```csharp
namespace Anela.Heblo.Xcc.Persistance;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}
```

**Acceptance criteria:**
- File exists at `backend/src/Anela.Heblo.Xcc/Persistance/PagedResult.cs`.
- Type is declared as a `public class` (not `record`) so existing object-initializer call sites (`new PagedResult<T> { Items = ..., TotalCount = ..., PageNumber = ..., PageSize = ... }`) compile unchanged.
- Nullable reference types remain enabled for the file (matches Xcc project settings).
- `dotnet build` of the `Anela.Heblo.Xcc` project succeeds.

### FR-2: Remove the duplicate declaration from the Journal domain
Delete the `public class PagedResult<T>` declaration from `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` (lines 26-32 in the current revision). The file must continue to expose `IJournalRepository` and nothing else from that namespace.

**Acceptance criteria:**
- `IJournalRepository.cs` no longer contains a `PagedResult<T>` type declaration.
- The file still imports `Anela.Heblo.Xcc.Persistance` so that `PagedResult<JournalEntry>` resolves to the new shared type.
- No other type named `PagedResult` remains anywhere under `Anela.Heblo.Domain/`.

### FR-3: Update consumers to reference the new namespace
Every consumer that previously resolved `PagedResult<T>` via `Anela.Heblo.Domain.Features.Journal` must now resolve it via `Anela.Heblo.Xcc.Persistance`. Concretely:

| File | Current behavior | Required behavior |
|---|---|---|
| `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs` | imports `Anela.Heblo.Domain.Features.Journal` only for `PagedResult<T>` | drop the `using Anela.Heblo.Domain.Features.Journal;` line; rely on the existing `using Anela.Heblo.Xcc.Persistance;` |
| `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs` | references `PagedResult<MarketingAction>` (lines 38, 107) | ensure `using Anela.Heblo.Xcc.Persistance;` is present; remove any redundant `using Anela.Heblo.Domain.Features.Journal;` that exists solely for `PagedResult<T>` |
| `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` | references `PagedResult<JournalEntry>` (lines 38, 70, 79, 152) | ensure `using Anela.Heblo.Xcc.Persistance;` is present |
| `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` | constructs `PagedResult<JournalEntry>` (lines 46, 117) | ensure `using Anela.Heblo.Xcc.Persistance;` is present |

**Acceptance criteria:**
- `IMarketingActionRepository.cs` no longer contains `using Anela.Heblo.Domain.Features.Journal;` (the only reason it was there was `PagedResult<T>`).
- A repo-wide search for `Anela.Heblo.Domain.Features.Journal` from a non-Journal feature folder returns zero hits.
- All four consumer files compile against the relocated type.
- The Marketing module has zero references — `using`, fully-qualified, or otherwise — to any type in `Anela.Heblo.Domain.Features.Journal`.

### FR-4: Preserve behavior end-to-end
This is a pure move; no method signatures, serialization shapes, JSON property names, paging semantics, or runtime behavior may change.

**Acceptance criteria:**
- `dotnet build` of the full solution succeeds with zero new warnings attributable to this change.
- `dotnet format` reports no remaining style issues in touched files.
- The Journal test `SearchJournalEntriesHandlerTests` (which constructs `PagedResult<JournalEntry>` directly) passes without test-code changes other than the `using` directive.
- All existing tests in the backend test suite pass: `dotnet test` is green.
- No API endpoint that returns paginated Journal or Marketing data changes its response payload shape (verified by inspecting controllers / handlers that surface these results; if a serialization contract test exists, it must still pass).

### FR-5: No new abstractions
Do not introduce `IPagedResult<T>`, a `PagedResult<T>` record variant, factory methods, extension methods, or any additional members. The scope is "move the existing class to a new namespace, fix imports." Anything beyond that is out of scope (see §Out of Scope).

**Acceptance criteria:**
- The diff contains exactly: one new file in Xcc, one deletion of the inline class in `IJournalRepository.cs`, and `using` directive adjustments in consumers.
- No public members are added to or removed from `PagedResult<T>`.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact is expected or permitted. The change is a namespace relocation with identical type shape. No allocation patterns, query plans, or serializer behavior should differ.

### NFR-2: Security
No security surface is affected. `PagedResult<T>` carries no auth, no PII, and no serialization-customization attributes. No new public endpoints, no DI registrations, no configuration.

### NFR-3: Backward compatibility
- **Wire compatibility:** Any HTTP response that currently embeds `PagedResult<T>` (directly or via a DTO) must produce byte-identical JSON before and after the change. Since the property names, order, and types are unchanged, this falls out of FR-1's "shape-compatible" rule — but it must be explicitly verified by running affected endpoints (or their integration tests) and comparing payloads if available.
- **Source compatibility for in-repo callers:** All in-repo callers will be updated in the same change. No third-party consumes this type. No deprecation shim is required.
- **Database / migration impact:** None. `PagedResult<T>` is not persisted.

### NFR-4: Maintainability
After the change, any new module that needs paginated repository methods imports `Anela.Heblo.Xcc.Persistance` (which it already imports for `IRepository<,>`). No cross-feature `using` directive is required.

## Data Model
No domain entities, database tables, or persistence schemas are added, removed, or modified.

The single moved type:

```
Anela.Heblo.Xcc.Persistance.PagedResult<T>
├── List<T> Items        (default = new())
├── int     TotalCount
├── int     PageNumber
└── int     PageSize
```

Lives next to `IRepository<TEntity, TKey>` and `IReadOnlyRepository<TEntity, TKey>` in `backend/src/Anela.Heblo.Xcc/Persistance/`.

## API / Interface Design

### Public type surface (after the change)
```csharp
namespace Anela.Heblo.Xcc.Persistance;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}
```

### Affected files (exhaustive)
1. `backend/src/Anela.Heblo.Xcc/Persistance/PagedResult.cs` — **new**.
2. `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` — remove the inline `PagedResult<T>` class; keep `using Anela.Heblo.Xcc.Persistance;` (already present).
3. `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs` — remove `using Anela.Heblo.Domain.Features.Journal;`.
4. `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs` — verify/add `using Anela.Heblo.Xcc.Persistance;`; remove the Journal `using` if it was there solely for `PagedResult<T>`.
5. `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — verify `using Anela.Heblo.Xcc.Persistance;` is present.
6. `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` — verify/add `using Anela.Heblo.Xcc.Persistance;`.

### Public HTTP / MediatR contracts
Unchanged. Handler signatures and controller routes that surface paginated results keep their existing CLR types; only the namespace of `PagedResult<T>` changes, which is invisible over the wire.

## Dependencies

### Build-time
- The `Anela.Heblo.Domain` project already depends on `Anela.Heblo.Xcc` (it imports `IRepository<,>` from it). No new project reference is required.
- The `Anela.Heblo.Persistence` project already depends on both `Anela.Heblo.Domain` and `Anela.Heblo.Xcc`. No new project reference is required.
- The `Anela.Heblo.Tests` project already references both. No new project reference is required.

### Runtime / external services
None.

### Tooling
- `dotnet build`, `dotnet format`, `dotnet test` — already part of the standard validation step from `CLAUDE.md`.
- OpenAPI TypeScript client regeneration: the generated client must be re-run if any OpenAPI-exposed type changes. `PagedResult<T>` is a CLR type used inside repository interfaces, not a controller response DTO surfaced through OpenAPI. **Assumption:** no OpenAPI schema entry is named `PagedResult` today; if regeneration produces a non-empty diff, that diff must be inspected and committed alongside the change.

## Out of Scope
The following are deliberately excluded from this change to keep the diff surgical and the blast radius minimal:

- Converting `PagedResult<T>` to a `record` or `sealed record`. (The current call sites use object-initializer syntax with settable properties; converting would force broader changes and could affect any serializer expectations.)
- Introducing an `IPagedResult<T>` interface.
- Adding helper factories (`PagedResult.Empty<T>()`, `PagedResult.From(...)`, etc.).
- Changing property names, types, or defaults.
- Adding XML doc comments, validation attributes, or `[JsonPropertyName]` annotations.
- Auditing or relocating other potential cross-module type leaks. (Such an audit may be warranted as a follow-up but is its own task.)
- Marking the old Journal-namespaced type as `[Obsolete]` and keeping a shim — there are no out-of-repo consumers, so no shim is needed.
- Updating frontend code or the generated TypeScript client — no public OpenAPI contract changes (see Dependencies note above).
- Adding new tests for `PagedResult<T>`. The type is a passive DTO with no logic; existing repository and handler tests already exercise it transitively.

## Open Questions
None.

## Status: COMPLETE
