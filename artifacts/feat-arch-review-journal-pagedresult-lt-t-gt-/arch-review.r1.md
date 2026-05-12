# Architecture Review: Relocate `PagedResult<T>` to the Xcc Shared Layer

## Skip Design: true

## Architectural Fit Assessment

This change is a **pure-fit** correction: it aligns the codebase with rules already documented in `docs/architecture/development_guidelines.md`, which explicitly forbids cross-module domain references and reserves `Xcc` for "technical concerns only." The target namespace `Anela.Heblo.Xcc.Persistance` already houses `IRepository<TEntity, TKey>` and `IReadOnlyRepository<TEntity, TKey>` — the exact peer abstractions `PagedResult<T>` belongs alongside. No new architectural concepts are introduced; the change retires an architectural anomaly.

Integration points are limited and verified:
- 5 referencing files in total (1 declaration, 4 consumers).
- `Anela.Heblo.Xcc.Persistance` is already imported by both `IJournalRepository.cs` and `IMarketingActionRepository.cs` for `IRepository<,>`, so the move adds *zero* new project or namespace dependencies anywhere.
- Marketing's domain interface is the only non-Journal feature folder that currently imports `Anela.Heblo.Domain.Features.Journal`. The remaining 23 hits are legitimate Journal-internal usages and stay untouched.
- No collision risk: a repo-wide search for `class PagedResult` / `record PagedResult` yields exactly one declaration (the one being moved).

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Xcc (cross-cutting / "technical concerns only")
└── Persistance/
    ├── IReadOnlyRepository<TEntity, TKey>     (existing)
    ├── IRepository<TEntity, TKey>             (existing)
    ├── EmptyRepository                        (existing)
    └── PagedResult<T>                         (NEW — moved here)

Anela.Heblo.Domain
└── Features/
    ├── Journal/
    │   └── IJournalRepository.cs              (PagedResult<T> declaration REMOVED)
    │       └── using Anela.Heblo.Xcc.Persistance;   (already present, now resolves PagedResult)
    └── Marketing/
        └── IMarketingActionRepository.cs
            └── using Anela.Heblo.Xcc.Persistance;   (already present)
            └── using Anela.Heblo.Domain.Features.Journal;   (REMOVED)

Anela.Heblo.Persistence
├── Catalog/Journal/JournalRepository.cs       (using Anela.Heblo.Xcc.Persistance; — verify/add)
└── Marketing/MarketingActionRepository.cs     (using Anela.Heblo.Xcc.Persistance; — verify/add;
                                                using Anela.Heblo.Domain.Features.Journal; — remove
                                                IF it was solely for PagedResult; currently it is
                                                kept for other Journal-internal types? — see Risk R3)
```

### Key Design Decisions

#### Decision 1: Location — `Anela.Heblo.Xcc/Persistance/` (not `Xcc/Paging/` or `Xcc/Common/`)
**Options considered:**
- (a) `Anela.Heblo.Xcc.Persistance.PagedResult<T>` — alongside `IRepository<,>`
- (b) New folder `Anela.Heblo.Xcc.Paging.PagedResult<T>`
- (c) `Anela.Heblo.Xcc.PagedResult<T>` (root namespace)

**Chosen approach:** (a) — co-locate with `IRepository<,>` in `Anela.Heblo.Xcc/Persistance/`.

**Rationale:** `PagedResult<T>` is consumed exclusively as a return type of repository methods. The companion contracts (`IRepository<,>`, `IReadOnlyRepository<,>`) already live there, and every consumer already has `using Anela.Heblo.Xcc.Persistance;`. Co-locating means **zero new `using` directives** are required in any file. Options (b) and (c) would create a brand-new namespace and force `using` additions in five files for no semantic benefit.

#### Decision 2: Type kind — `public class` (not `record`)
**Options considered:** `public class` vs. `public sealed record` vs. `public sealed record class`.

**Chosen approach:** `public class` with settable properties, byte-identical shape to the current declaration.

**Rationale:** Project rule (`CLAUDE.md`): *"DTOs are classes, never C# records — OpenAPI client generators mishandle record parameter order."* Even though `PagedResult<T>` is not currently surfaced through OpenAPI, it *transits* through MediatR handler return types and may end up serialized if a controller composes paginated responses. Keeping `class` semantics also preserves the object-initializer call sites at `MarketingActionRepository.cs:107-113`, `JournalRepository.cs:70-76`, `JournalRepository.cs:152-158`, and `SearchJournalEntriesHandlerTests.cs:46-52, 117-123`. Spec FR-1 and FR-5 explicitly forbid switching to `record`; the rationale here documents *why*.

#### Decision 3: No interface, no helpers
**Chosen approach:** Do not introduce `IPagedResult<T>`, factories, extension methods, or an `Empty` static.

**Rationale:** YAGNI. The spec calls out a surgical move. Adding ergonomics now would expand the diff, invite test changes, and risk subtly altering serialization semantics. Future need can be addressed in a follow-up.

#### Decision 4: No deprecation shim in the old namespace
**Options considered:** Leave a `[Obsolete]` `PagedResult<T>` in `Anela.Heblo.Domain.Features.Journal` for one release vs. hard cut-over.

**Chosen approach:** Hard cut-over — delete the old declaration in the same commit.

**Rationale:** Solo-developer repo, no external consumers, all five call sites updated atomically. A shim would re-introduce the very cross-module coupling we are removing. Spec NFR-3 confirms no deprecation needed.

## Implementation Guidance

### Directory / Module Structure

**Create:**
```
backend/src/Anela.Heblo.Xcc/Persistance/PagedResult.cs
```

**Modify (deletion only):**
```
backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs  (lines 26-32 removed)
```

**Modify (using-directive housekeeping):**
```
backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs
backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs
backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs
```

No project references change. No `Module.cs` registrations change. No DI wiring touched.

### Interfaces and Contracts

The new file should mirror exactly the existing shape — no additions, no removals, no attributes, no XML docs:

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

File-scoped namespace matches the convention of the neighbouring `IRepository.cs` (line 3) and `IReadOnlyRepository.cs` (line 4).

### Data Flow

Runtime data flow is **unchanged**. The only thing that changes is *which* assembly's metadata holds the `PagedResult<T>` CLR type token.

```
HTTP request
  → Controller
    → MediatR handler (Anela.Heblo.Application.Features.Journal | Marketing)
      → IJournalRepository | IMarketingActionRepository  (returns PagedResult<T> — now resolved from Xcc.Persistance)
        → JournalRepository | MarketingActionRepository
          → EF Core query → new PagedResult<T> { Items, TotalCount, PageNumber, PageSize }
      ← PagedResult<T>
    ← Handler response DTO (still owned by feature's Contracts folder)
  ← JSON serialization (property names Items/TotalCount/PageNumber/PageSize — unchanged)
```

The serialized wire shape is identical because the property names, order of declaration, types, and access modifiers do not change. System.Text.Json (and Newtonsoft, if used anywhere) keys off property names, not declaring-namespace.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| **R1.** A consumer references `PagedResult<T>` via fully-qualified name `Anela.Heblo.Domain.Features.Journal.PagedResult` (not surfaced by `using`-only search). | Low | After the move, run `grep -RnE "Features\.Journal\.PagedResult" backend/` to confirm zero hits. The earlier scan of all 5 referencing files showed only unqualified `PagedResult<T>` usage — risk is low but worth a final verification step. |
| **R2.** OpenAPI/TypeScript client regeneration produces a non-empty diff (e.g., schema named `PagedResult` exposed via a controller we missed). | Low | Spec NFR-3 / Dependencies already mandate inspecting the regenerated client. If a diff appears, inspect it before committing; if it is purely a `$ref` namespace change without altered JSON, accept and commit. If it introduces *renamed* schemas, escalate — do not silently absorb the rename. |
| **R3.** Marketing infrastructure file (`MarketingActionRepository.cs`) currently imports `Anela.Heblo.Domain.Features.Journal` for reasons other than `PagedResult<T>`. | Low | Verified: line 1 imports `Anela.Heblo.Domain.Features.Journal` and the file does **not** reference any other Journal type. After removing `PagedResult<T>` resolution from there, the entire `using` line is removable. Run `dotnet build` to confirm no transitive symbol resolution depended on it. The Marketing *interface* file (`IMarketingActionRepository.cs`) also imports the Journal namespace solely for `PagedResult<T>` — same removal applies. |
| **R4.** Future cross-module imports of `Anela.Heblo.Domain.Features.Journal` from non-Journal folders re-introduce the violation. | Low (procedural) | Out of scope for this change, but worth filing a follow-up: an architecture test (e.g., NetArchTest) asserting *"no `Anela.Heblo.Domain.Features.<X>` namespace is referenced from a sibling `Features.<Y>` folder."* Mentioned here so it doesn't get forgotten. |
| **R5.** Existing handler/controller integration tests fail due to a serializer or reflection-based contract test pinning the declaring assembly of `PagedResult<T>`. | Very low | Spec NFR-3 already requires confirming wire compatibility. Run `dotnet test` for the full backend suite; if any test inspects `Type.FullName` or `Assembly.GetType("...PagedResult`1")`, update it as part of this change. None expected. |
| **R6.** `dotnet format` reorders or rewrites `using` directives in touched files, accidentally re-adding the Journal `using` via auto-restore. | Very low | After edits, run `dotnet format` then `grep -n "Features.Journal" backend/src/Anela.Heblo.Domain/Features/Marketing/` and confirm zero hits. Formatters do not re-add removed usings — this is mostly belt-and-braces. |

## Specification Amendments

The spec is correct, complete, and surgical. Two minor clarifications to add for the implementer:

1. **FR-3, MarketingActionRepository.cs row:** Confirmed by reading the file — the only `Anela.Heblo.Domain.Features.Journal` import is at line 1 and it is used *exclusively* for `PagedResult<T>`. The spec's "remove any redundant `using` ... that exists solely for `PagedResult<T>`" should be hardened to **"remove the `using Anela.Heblo.Domain.Features.Journal;` directive on line 1 (verified: no other Journal types are referenced in this file)."**

2. **Verification step (post-amendment to FR-3):** After all edits, run a project-wide check:
   ```
   grep -RnE "Features\.Journal" backend/src/Anela.Heblo.Domain/Features/Marketing backend/src/Anela.Heblo.Persistence/Marketing
   ```
   Expected output: zero lines. Add this as an explicit acceptance check under FR-3.

3. **FR-1 file header:** The neighbouring files (`IRepository.cs`, `IReadOnlyRepository.cs`) use file-scoped namespace syntax. The spec's sample uses the same — good. Implementer should match the surrounding style exactly (file-scoped namespace, no `using System.Collections.Generic;` needed since the project's `ImplicitUsings` is enabled — verify by inspecting the `.csproj`; if not enabled, add the using).

No structural changes to scope, contracts, or out-of-scope items are required.

## Prerequisites

None. All required infrastructure is in place:

- ✅ `Anela.Heblo.Xcc` project exists and is referenced by `Anela.Heblo.Domain`, `Anela.Heblo.Persistence`, and `Anela.Heblo.Tests`.
- ✅ Target namespace `Anela.Heblo.Xcc.Persistance` exists and already holds peer types.
- ✅ Every consumer file already has `using Anela.Heblo.Xcc.Persistance;` declared.
- ✅ No database migration, no config change, no DI registration, no feature flag.
- ✅ No coordination with other in-flight branches required — diff is ~6 files, fully self-contained.

Implementation may begin immediately.