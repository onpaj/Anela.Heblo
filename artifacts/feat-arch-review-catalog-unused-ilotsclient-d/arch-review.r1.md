I have enough context. Now producing the architecture review.

# Architecture Review: Remove Unused `ILotsClient` Dependency from `GetCatalogDetailHandler`

## Skip Design: true

This is a backend-only refactor (constructor signature and field removal). No UI, layout, or visual component changes.

## Architectural Fit Assessment

The change aligns perfectly with project conventions:

- **Vertical Slice Architecture** (`docs/architecture/development_guidelines.md`): the handler lives in its own use-case folder under `Features/Catalog/UseCases/GetCatalogDetail/`. The change is localized to that slice — no cross-module impact.
- **DI conventions** (`csharp-patterns.md` → "Keep constructors focused"): removing an unused dependency directly satisfies the rule.
- **YAGNI / KISS** (global coding-style rules): the dependency is dead code; deletion is the correct move.
- **No contract change**: `GetCatalogDetailRequest` / `GetCatalogDetailResponse` are untouched, so module-boundary contracts (`Application/Features/Catalog/Contracts`) remain stable.
- **DI registration**: `ILotsClient` is registered in `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` and consumed by `CatalogDataRefreshService` and `CatalogRepository` (via `CatalogRepositoryCacheOptimizationTests` / `CatalogRepositoryTests`). The registration must remain.

Verified integration points (grep results):
- `_lotsClientMock` is referenced in `GetCatalogDetailHandlerTests.cs` (lines 21, 29, 33) and `GetCatalogDetailHandlerFullHistoryTests.cs` (lines 21, 29, 33) — both purely for handler construction; no `Setup` / `Verify` calls. These three lines per file are the only test-side touch points.
- `CatalogRepositoryTests.cs` and `CatalogRepositoryCacheOptimizationTests.cs` also reference `_lotsClientMock` but those tests cover a different class (`CatalogRepository`) and **must not be modified**.

## Proposed Architecture

### Component Overview

```
HTTP / MediatR pipeline
   │
   ▼
GetCatalogDetailHandler  ── depends on ──► ICatalogRepository ──► CatalogAggregate (cached)
   │                                                                  │
   │                                                                  └── Stock.Lots (already populated)
   │
   ├── IMapper (AutoMapper)
   ├── TimeProvider
   └── ILogger<GetCatalogDetailHandler>

   ✗ ILotsClient  (REMOVED — never invoked; lots come from CatalogAggregate.Stock.Lots)
```

`ILotsClient` itself remains a domain abstraction (`Domain/Features/Catalog/Lots/ILotsClient.cs`) and continues to be consumed by `CatalogDataRefreshService` and `CatalogRepository`, which populate the aggregate's `Stock.Lots` ahead of time. The handler simply reads the resulting cache.

### Key Design Decisions

#### Decision 1: Surgical constructor edit, no broader refactor
**Options considered:**
1. Remove only the `ILotsClient` parameter and field (surgical).
2. Audit and remove all unused dependencies across Catalog handlers in one pass.
3. Introduce a handler-base or builder to standardize dependency wiring.

**Chosen approach:** Option 1 — surgical edit limited to `GetCatalogDetailHandler` and its two direct test fixtures.

**Rationale:** Aligns with CLAUDE.md "Surgical changes — touch only what the task requires." Spec explicitly scopes other handlers as out of scope (Out of Scope §3, §4). The brief originates from an arch-review finding for this single handler; broader sweeps belong in their own arch-review tickets.

#### Decision 2: Remove the test-side `ILotsClient` mock entirely (not pass `null`)
**Options considered:**
1. Remove `_lotsClientMock` field, `new Mock<ILotsClient>()` line, and the constructor argument from both handler test files.
2. Keep the mock field but stop passing it (dead test-side code).
3. Pass `null!` to preserve the existing constructor argument count.

**Chosen approach:** Option 1 — full removal of mock field, instantiation, and constructor argument.

**Rationale:** Option 3 is not viable — the parameter is being deleted, so there is nothing to pass. Option 2 leaves dead code in tests, contradicting the YAGNI motivation of the change itself. Option 1 keeps the test fixture honest about what the handler actually depends on.

#### Decision 3: Remove the now-orphaned `using Anela.Heblo.Domain.Features.Catalog.Lots;` directive in the handler
**Options considered:**
1. Remove the using if no other type from that namespace is referenced.
2. Leave the using for "future use."

**Chosen approach:** Option 1.

**Rationale:** FR-1 acceptance criterion explicitly requires no orphaned usings. `dotnet format` / IDE cleanup enforces this. Confirmed by reading the handler (line 4 of `GetCatalogDetailHandler.cs`) — `ILotsClient` is the only `Lots` namespace member used. In the **test files**, `using ...Catalog.Lots;` is also used by `CatalogLot` references elsewhere in some catalog tests — but for `GetCatalogDetailHandlerTests.cs` and `GetCatalogDetailHandlerFullHistoryTests.cs` specifically, only `ILotsClient` is used from that namespace, so the using directive can also be dropped in those two test files.

## Implementation Guidance

### Directory / Module Structure
No new files. Only edits to existing files:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs` | Remove field (line 14), constructor parameter (line 21), assignment (line 27), and the `using Anela.Heblo.Domain.Features.Catalog.Lots;` directive (line 4). |
| `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerTests.cs` | Remove `_lotsClientMock` field (line 21), instantiation (line 29), and constructor argument (line 33). Remove `using ...Catalog.Lots;` (line 9) if no other reference. |
| `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs` | Same as above (lines 21, 29, 33; using on line 8). |

**Do not touch:**
- `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs` — `_lotsClientMock` here belongs to `CatalogRepository`, not the handler.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs` — same reason.
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` — DI registration must remain (FR-4).
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs` — legitimate `ILotsClient` consumer.

### Interfaces and Contracts
No interface changes. `ILotsClient` remains in `backend/src/Anela.Heblo.Domain/Features/Catalog/Lots/ILotsClient.cs` (a single-method interface; safe to leave unchanged). MediatR request/response DTOs (`GetCatalogDetailRequest`, `GetCatalogDetailResponse`) are unchanged.

### Data Flow
Unchanged. Confirmed by reading `GetCatalogDetailHandler.cs` lines 36–70:

1. `CatalogDataRefreshService` populates `CatalogAggregate.Stock.Lots` ahead of time using `ILotsClient`.
2. `_catalogRepository.SingleOrDefaultAsync(...)` returns the cached aggregate.
3. Handler maps the aggregate to `CatalogItemDto`.
4. When `catalogItem.HasLots` is true, `catalogItemDto.Lots` is populated from `catalogItem.Stock.Lots.Select(...)` — no client call.
5. Response returned.

The handler never reads `_lotsClient` (verified by reading the entire 257-line file). Removal is provably side-effect-free.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Other handlers/services also inject `ILotsClient` legitimately and could be confused with this one | Low | Scope is explicit (FR-4): only this handler's injection is removed. DI registration and other consumers remain. Confirmed by grep — `CatalogDataRefreshService` and `CatalogRepository` are the legitimate consumers. |
| Removing the test-side mock breaks unrelated tests | Low | Confirmed via grep: only `GetCatalogDetailHandlerTests` and `GetCatalogDetailHandlerFullHistoryTests` instantiate the handler. `CatalogRepository` tests' `_lotsClientMock` is for a different SUT and must not be touched. |
| Reflection-based DI containers (e.g. constructor selection) pick a different constructor after edit | Negligible | The class declares a single public constructor; no reflection-based selection ambiguity. |
| Future feature work re-introduces on-demand lot fetching and needs the client back | Low | If/when that requirement materializes, re-injecting is trivial (one parameter). YAGNI applies now. |
| `dotnet format` reorders or rewrites adjacent unrelated lines | Low | Run `dotnet format` only on the touched files; commit formatting separately if it produces incidental changes. |

## Specification Amendments

The spec is COMPLETE and correctly scoped. Two clarifications to add for the implementer:

1. **Explicit list of test fixtures to update** (refines FR-3): only `GetCatalogDetailHandlerTests.cs` and `GetCatalogDetailHandlerFullHistoryTests.cs`. The handler test fixtures use no `Setup()` or `Verify()` calls on `_lotsClientMock`, so the mock instance can be removed wholesale (field, instantiation, and constructor argument) — no test bodies need editing.

2. **Using-directive cleanup** (refines FR-1 acceptance criterion #3): in both the handler file and the two test files, the `using Anela.Heblo.Domain.Features.Catalog.Lots;` directive should be removed since it would become unused. Verify with the IDE/`dotnet format`.

## Prerequisites

None. The change requires no migrations, configuration, infrastructure, feature flags, or coordinated rollout. It is a pure compile-time refactor.

**Pre-flight validation commands** (from project CLAUDE.md):
- `dotnet build` — must succeed.
- `dotnet format` — apply to touched files; commit any formatting deltas.
- `dotnet test --filter "FullyQualifiedName~GetCatalogDetailHandler"` — both `GetCatalogDetailHandlerTests` and `GetCatalogDetailHandlerFullHistoryTests` must pass without modification beyond the three constructor-related lines per file.