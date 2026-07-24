# Architecture: Move AnalyticsRepository out of Persistence

## Verdict

The relocation itself is correct and low-risk. However, **the target path chosen by plan-01.md / design-01.md (`Features/Analytics/Infrastructure/AnalyticsRepository.cs`) is not the best-supported location.** There is a closer, more direct precedent in the codebase — `CatalogRepository` — that places the analogous class one level up, directly under `Features/{Feature}/`, sibling to `Infrastructure/` rather than inside it. This document steers implementation to that location instead.

## Evidence

`docs/architecture/filesystem.md`'s Application-layer placement rules list two *distinct* bullets, not one:

```
- Features/{Feature}/Infrastructure/: Feature-specific infrastructure
- Features/{Feature}/{Feature}Repository.cs: Repository implementations
```

These aren't interchangeable. Checking actual usage confirms the split is real, not just documentation noise:

- `find backend/src/Anela.Heblo.Application/Features -maxdepth 2 -iname "*Repository.cs"` → only `Catalog/CatalogRepository.cs`. It is the **only existing precedent** for a non-DB, composing "Repository" class that has already made the Persistence→Application move `AnalyticsRepository` is now making.
- `CatalogRepository` sits at `Features/Catalog/CatalogRepository.cs`, namespace `Anela.Heblo.Application.Features.Catalog` — **not** `Features.Catalog.Infrastructure`. It composes `CatalogCacheStore`, `CatalogMergeService`, `CatalogDataRefreshService`, `ICatalogMergeScheduler` — all of which *do* live in `Features/Catalog/Infrastructure/`.
- Every `Infrastructure/` folder sampled (`Invoices/Infrastructure/`, `Bank/Infrastructure/`) holds adapters, schedulers, jobs, clients — the things a repository *depends on* — never the repository/facade class itself.

So the codebase's actual convention is: `Infrastructure/` holds the technical building blocks; the `{Feature}Repository.cs` that composes them sits one level up, at the feature root. `AnalyticsRepository` is exactly this shape — a `sealed class AnalyticsRepository : IAnalyticsRepository` composing three source interfaces — so it should follow `CatalogRepository`'s placement, not the generic "Infrastructure adapter" placement the design cited.

The design's claim that "20+ analogous non-DB adapters" already live in `Infrastructure/` is true but not the relevant precedent — those are adapter/scheduler classes (`InvoiceConsumptionSourceAdapter`, `*ImportJob`), not `*Repository` classes. `CatalogRepository` is the only apples-to-apples comparison, and it disagrees with the design's chosen path.

## Corrected architecture

### 1. `AnalyticsRepository` — new location

```
backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsRepository.cs
namespace Anela.Heblo.Application.Features.Analytics;
```

(Not `Features/Analytics/Infrastructure/...` / `...Analytics.Infrastructure` as plan-01.md and design-01.md specify.)

All four members, bodies, and XML doc comments stay byte-for-byte identical — only the `namespace` line changes, same as before.

### 2. `AnalyticsModule.cs` — registration site

`AnalyticsModule.cs` already declares `namespace Anela.Heblo.Application.Features.Analytics;`. Once `AnalyticsRepository` moves into that same namespace, **no `using` is needed at all** — this is simpler than the plan's proposed `using Anela.Heblo.Application.Features.Analytics.Infrastructure;` swap. The fix becomes:

- Delete the line `using Anela.Heblo.Persistence.Features.Analytics;` outright (no replacement).
- Update the stale comment: `// Repository (implementation lives in the Persistence layer)` → `// Repository implementation below in this namespace` (or simply drop the parenthetical — the class is now local to the file's own namespace, so a location comment is arguably no longer needed; keep it short if kept at all).

The DI line itself is unchanged: `services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();`

### 3. `AnalyticsRepositoryTests.cs` — test file

The test currently sits at `backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsRepositoryTests.cs` — directly under `Features/Analytics/`, **not** in an `Infrastructure/` subfolder. Since the corrected source location is also directly under `Features/Analytics/` (no `Infrastructure/` segment), **the test file does not need to move at all.** Only its `using` changes:

- `using Anela.Heblo.Persistence.Features.Analytics;` → `using Anela.Heblo.Application.Features.Analytics;`

This is a strictly smaller diff than design-01.md's proposal (which invented a new `test/.../Features/Analytics/Infrastructure/` folder). design-01.md's precedent argument ("every sibling module's Infrastructure/ source folder has a matching Infrastructure/ test folder") is true but doesn't apply here, because the corrected source path has no `Infrastructure/` segment to mirror.

## File-level diff summary (corrected)

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs` | deleted (moved) — folder becomes empty; delete the now-empty `Persistence/Features/Analytics/` directory too |
| `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsRepository.cs` | new — same content, `namespace Anela.Heblo.Application.Features.Analytics;` |
| `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` | `using Anela.Heblo.Persistence.Features.Analytics;` deleted (no replacement); stale comment corrected |
| `backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsRepositoryTests.cs` | stays in place; `using` swapped to `Anela.Heblo.Application.Features.Analytics` |

## Alignment with plan-01.md / design-01.md

Everything else in the plan and design holds: this is a pure relocation, no logic/contract/behavior change, `IAnalyticsRepository` (Domain) and the three source interfaces are untouched, and FR-4 (no other consumers) still applies unchanged. Only the **target path** (FR-1/FR-2 in the plan, section 1–3 in the design) is corrected. Implementers should treat this document as amending those two artifacts' target-path decision; the rest of their acceptance criteria and rough-plan steps apply verbatim with `Infrastructure/` / `.Infrastructure` struck from every path and namespace they mention for `AnalyticsRepository`.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Leaving `Persistence/Features/Analytics/` as an empty directory after the move | Delete the empty directory in the same commit — git doesn't track empty dirs, so this is automatic once the one file inside is moved. |
| `AnalyticsModule.cs`'s `using` removal breaks another type in the file that still needs `Anela.Heblo.Persistence.Features.Analytics` | Grep confirms `AnalyticsRepository` is the only symbol from that namespace referenced in the file; removing the `using` is safe. Verify with `dotnet build` regardless. |
| Namespace collision: `Anela.Heblo.Application.Features.Analytics` now holds both `AnalyticsModule` and `AnalyticsRepository` in the same namespace, differing only by file | Not a conflict — C# allows any number of types per namespace; this mirrors `CatalogModule`/`CatalogRepository` coexisting in `Anela.Heblo.Application.Features.Catalog` today. |
| Future readers expect `Infrastructure/` per the finding's literal suggested-fix text | The finding's suggested fix is a starting hypothesis, not a mandate — `docs/architecture/filesystem.md` and the one real precedent (`CatalogRepository`) both support the flatter placement. Worth a one-line note in the PR description so reviewers aren't surprised the path differs from the filed issue text. |

## Prerequisites

None beyond what plan-01.md already states. This is self-contained: `git mv`, two namespace-only edits, one `using` deletion, one comment edit, `dotnet build && dotnet format`, run the Analytics test filter.
