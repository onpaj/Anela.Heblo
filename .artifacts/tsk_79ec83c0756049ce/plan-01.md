# Plan: Move AnalyticsRepository out of Persistence

## Summary

`AnalyticsRepository` (`Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs`) does no database work — it is a pure delegating adapter over three cross-module source interfaces (`IAnalyticsProductSource`, `IInvoiceImportStatisticsSource`, `IBankStatementStatisticsSource`), owned by Analytics itself. It is relocated to `Anela.Heblo.Application/Features/Analytics/Infrastructure/`, matching the established convention already used by 20+ other feature modules (Packaging, Invoices, Catalog, Bank, Manufacture, etc.), each of which keeps non-DB feature adapters in an `Infrastructure/` subfolder rather than in `Persistence`.

## Context

Per `docs/architecture/filesystem.md`, `Anela.Heblo.Persistence` is the infrastructure layer for "database contexts, configurations, shared repository implementations" — i.e. things that talk to the DB. `Application/Features/{Feature}/Infrastructure/` is the documented, precedented home for feature-owned, non-DB infrastructure adapters. `AnalyticsRepository` has zero `ApplicationDbContext`/EF Core/SQL usage; its four methods are 1:1 forwards (two identical pass-throughs, two adding only `.ToList()`) to interfaces Analytics already owns. Its current location forces the Application-layer `AnalyticsModule.cs` to reach into `Persistence` just to name a concrete type — a Clean Architecture layering smell this move removes. This is a pure relocation; no behavior changes.

## Functional requirements

**FR-1 — Relocate the class file**
Move `backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs` to `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs`, updating only the `namespace` declaration (`Anela.Heblo.Persistence.Features.Analytics` → `Anela.Heblo.Application.Features.Analytics.Infrastructure`). No logic, member signatures, or XML doc comments change.
- Acceptance: `git mv` diff shows the file moved with only the namespace line changed; `grep -r "Persistence.Features.Analytics"` in `backend/src` returns nothing referencing `AnalyticsRepository`.

**FR-2 — Update the registration site**
In `AnalyticsModule.cs`, replace `using Anela.Heblo.Persistence.Features.Analytics;` with `using Anela.Heblo.Application.Features.Analytics.Infrastructure;`. The `services.AddScoped<IAnalyticsRepository, AnalyticsRepository>()` line and its comment stay as-is (or the stale "implementation lives in the Persistence layer" comment is corrected to reflect the new location — see FR-2a).
- Acceptance: `dotnet build` succeeds; `AnalyticsModule.cs` no longer has a `using` referencing `Anela.Heblo.Persistence.*` for this type.

**FR-2a — Fix the now-inaccurate comment**
Update the comment above the DI registration (`// Repository (implementation lives in the Persistence layer)`) to reflect the new location, e.g. `// Repository (implementation lives in Application/Features/Analytics/Infrastructure)`.
- Acceptance: comment text matches actual location.

**FR-3 — Update the existing unit test's namespace/using**
`backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsRepositoryTests.cs` currently has `using Anela.Heblo.Persistence.Features.Analytics;`. Update to `using Anela.Heblo.Application.Features.Analytics.Infrastructure;`. Optionally relocate the test file to mirror the new path convention (`test/.../Features/Analytics/Infrastructure/AnalyticsRepositoryTests.cs`) if the codebase's test layout convention expects tests to mirror source folders — verify against a sibling example (e.g. Bank or Invoices `Infrastructure` test placement) before deciding; otherwise leave the test file where it is and only fix the `using`.
- Acceptance: `dotnet test --filter AnalyticsRepositoryTests` passes.

**FR-4 — No other consumers to touch**
Confirm no other file references `Anela.Heblo.Persistence.Features.Analytics` for `AnalyticsRepository` (handlers and `InvoiceImportStatisticsTile` already depend only on `IAnalyticsRepository` from `Anela.Heblo.Domain.Features.Analytics`, which is unaffected by this move).
- Acceptance: `grep -rn "Anela.Heblo.Persistence.Features.Analytics"` in `backend/` returns zero hits after the change.

## Non-functional requirements

- **No behavioral/perf change**: this is a pure move; runtime behavior, DI lifetime (`Scoped`), and method bodies are unchanged.
- **Build hygiene**: `dotnet build` and `dotnet format` must pass with no new warnings introduced by the move (e.g. no leftover unused `using`).

## Data model

Not applicable — no entities, DTOs, or schema are touched. `IAnalyticsRepository` (Domain layer) and the three source interfaces (`IAnalyticsProductSource`, `IInvoiceImportStatisticsSource`, `IBankStatementStatisticsSource`) keep their current locations/contracts unchanged.

## Interfaces

Not applicable — no controller, MediatR request/response, or event contracts are touched. Only a C# namespace and file path change.

## Dependencies and scope

**In scope:**
- Move `AnalyticsRepository.cs` from Persistence to `Application/Features/Analytics/Infrastructure/`.
- Update `AnalyticsModule.cs` using statement and stale comment.
- Update `AnalyticsRepositoryTests.cs` using statement (and possibly file location, per FR-3).

**Explicitly out of scope:**
- Deleting `AnalyticsRepository` entirely and injecting the three source interfaces directly into handlers/`InvoiceImportStatisticsTile` (the finding's alternative suggestion) — this is a larger behavioral refactor touching 5 handler files and a dashboard tile; not pursued here. Flagged as a possible follow-up in Open Questions.
- Any change to `IAnalyticsProductSource`, `IInvoiceImportStatisticsSource`, `IBankStatementStatisticsSource`, or their implementations.
- Any change to `IAnalyticsRepository`'s contract in the Domain layer.

**Depends on:** nothing external; self-contained within the Analytics module's three affected files (+ test).

## Rough plan

1. `git mv backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs`.
2. Update the `namespace` line in the moved file to `Anela.Heblo.Application.Features.Analytics.Infrastructure`.
3. Update `AnalyticsModule.cs`: swap the `using`, fix the stale comment.
4. Update `AnalyticsRepositoryTests.cs`: swap the `using` (relocate file only if it matches an established test-layout convention found in a sibling module).
5. `grep -rn "Anela.Heblo.Persistence.Features.Analytics"` across `backend/` to confirm zero remaining references.
6. Run `dotnet build` and `dotnet format` (per repo validation rules); run the Analytics test suite (`dotnet test --filter FullyQualifiedName~Analytics`).

## Open questions

- Should the "delete and inject sources directly" alternative be filed as a separate follow-up task? Default taken here: no — scope this task to the pure move only, since the finding presents it as an "alternatively" option and the move is the lower-risk, directly-actionable fix.
- Should the test file physically move to an `Infrastructure/` subfolder under `test/.../Features/Analytics/`? Default taken here: only move it if a sibling module's test suite demonstrably mirrors `Infrastructure/` folders on the test side; otherwise leave it in place and fix only the `using` — avoids speculative restructuring beyond what the finding asked for.
