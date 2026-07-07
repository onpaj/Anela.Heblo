# Specification: Remove orphaned InvoiceClassification statistics dead code

## Summary
`ClassificationHistoryRepository.GetStatisticsAsync` and its supporting domain/contract types (`ClassificationStatistics`, `RuleUsageStatistic`, `ClassificationStatisticsDto`, `RuleUsageStatisticDto`) were scaffolded for a statistics feature that was never finished: there is no interface declaration, no use case, no controller endpoint, and no working frontend consumer. This spec removes the dead code to keep the persistence layer, contract surface, and generated OpenAPI client honest about what the module actually does.

## Background
An automated architecture review (GitHub issue #3523) flagged that `ClassificationHistoryRepository` (`backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs`, lines 81–121) implements a public method `GetStatisticsAsync(DateTime? fromDate, DateTime? toDate)` that is **not** declared on `IClassificationHistoryRepository` (`backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationHistoryRepository.cs`). Since all consumers of the repository go through the interface (confirmed: `GetClassificationHistoryHandler` and `InvoiceClassificationService` are the only two consumers, both injecting `IClassificationHistoryRepository`), the method is unreachable in practice and effectively dead code.

Codebase exploration (on the `feature-3523-...` worktree) confirms the full extent of the orphaned scaffolding:

- **Domain types**: `ClassificationStatistics.cs` and `RuleUsageStatistic.cs` in `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/` exist solely to support `GetStatisticsAsync`'s return type.
- **Contract types**: `ClassificationStatisticsDto.cs` and `RuleUsageStatisticDto.cs` in `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/` are AutoMapper targets for the domain types but are never returned by any controller action, so they still leak into the OpenAPI spec and the generated TypeScript client surface as unused shapes... actually they do **not** currently appear in `frontend/src/api/generated/api-client.ts` at all (verified via grep — no `ClassificationStatisticsDto`, `RuleUsageStatisticDto`, or `invoiceClassification_.*[Ss]tatistic*` methods are generated), because AutoMapper mapping profile registration alone does not add a type to the OpenAPI schema; only types reachable from a controller action's request/response do. So the DTOs currently pollute only the C# contract surface, not (yet) the generated frontend client.
- **Mapping profile**: `InvoiceClassificationMappingProfile.cs` (`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/`) registers `CreateMap<ClassificationStatistics, ClassificationStatisticsDto>()` and `CreateMap<RuleUsageStatistic, RuleUsageStatisticDto>()` — both mappings are unused at runtime since nothing ever maps these types.
- **No use case / handler**: no `GetClassificationStatistics` request/handler/response exists anywhere under `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/`.
- **No controller endpoint**: `backend/src/Anela.Heblo.API/Controllers/InvoiceClassificationController.cs` has no statistics action (confirmed via grep — zero matches for "statistics"/"Statistics").
- **No working frontend consumer**: `frontend/src/api/hooks/useInvoiceClassification.ts` defines a query-key constant `statistics: ['invoice-classification', 'statistics']` inside `CLASSIFICATION_QUERY_KEYS`, but there is no `useClassificationStatistics` hook (or any hook) that uses this key, and no generated API client method exists to call. This query key is itself dead code that should be removed as part of this cleanup.
- **No tests**: a repo-wide search of `backend/test` found zero references to `GetStatisticsAsync`, `ClassificationStatistics`, or `RuleUsageStatistic*`, so no test code depends on any of this.
- **No DI-level surprises**: `IClassificationHistoryRepository` is registered once in `InvoiceClassificationModule.cs` (`services.AddScoped<IClassificationHistoryRepository, ClassificationHistoryRepository>()`); removing the concrete method has no DI implications since it isn't part of the interface contract.

Per the guidance accompanying this brief: this is a maintainability cleanup, not a product feature request. There is no evidence of product demand (no handler, no controller, no working frontend hook, no other issue requesting statistics). **Option B (remove dead code)** is the correct choice — finishing the feature (Option A) would mean building a use case, controller endpoint, and frontend UI speculatively, with no requirements gathered for what the statistics view should actually look like (date range picker? chart? dashboard tile?). That is out of scope for an arch-review cleanup and should go through the normal brainstorming process if/when there is real demand.

## Functional Requirements

### FR-1: Remove `GetStatisticsAsync` from `ClassificationHistoryRepository`
Delete the `GetStatisticsAsync(DateTime? fromDate, DateTime? toDate)` method (lines 81–121) from `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs`. Since the method is not declared on `IClassificationHistoryRepository`, no interface change is needed — only the concrete class implementation is removed.

**Acceptance criteria:**
- `ClassificationHistoryRepository.cs` no longer contains a `GetStatisticsAsync` method.
- `IClassificationHistoryRepository.cs` is unchanged (it never declared this method, so nothing to remove there).
- The class still implements `IClassificationHistoryRepository` correctly (compiles) with only `AddAsync`, `GetHistoryAsync`, `GetHistoryByInvoiceIdAsync`, and `GetPagedHistoryAsync`.

### FR-2: Remove orphaned domain types
Delete the following files entirely:
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ClassificationStatistics.cs`
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/RuleUsageStatistic.cs`

**Acceptance criteria:**
- Both files no longer exist in the repository.
- No remaining reference to `ClassificationStatistics` or `RuleUsageStatistic` anywhere in `backend/src` (verified by repo-wide search after the change).

### FR-3: Remove orphaned contract (DTO) types
Delete the following files entirely:
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/ClassificationStatisticsDto.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/RuleUsageStatisticDto.cs`

**Acceptance criteria:**
- Both files no longer exist in the repository.
- No remaining reference to `ClassificationStatisticsDto` or `RuleUsageStatisticDto` anywhere in `backend/src`.

### FR-4: Remove now-invalid AutoMapper mappings
In `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationMappingProfile.cs`, remove the two lines:
```csharp
CreateMap<ClassificationStatistics, ClassificationStatisticsDto>();
CreateMap<RuleUsageStatistic, RuleUsageStatisticDto>();
```
Leave all other mappings (`ClassificationRule`, `ClassificationHistory`, `AccountingTemplate`, `ReceivedInvoiceItem`, `ReceivedInvoice`) untouched.

**Acceptance criteria:**
- The mapping profile compiles without the two removed `CreateMap` calls (their type references no longer exist after FR-2/FR-3, so leaving them would break the build).
- AutoMapper configuration validation (if run in tests/startup) passes with no missing-mapping errors introduced by this change.

### FR-5: Remove dead frontend query-key entry
In `frontend/src/api/hooks/useInvoiceClassification.ts`, remove the unused `statistics` entry from `CLASSIFICATION_QUERY_KEYS`:
```ts
statistics: ['invoice-classification', 'statistics'] as const,
```
No hook currently uses this key, and no generated API client method exists to back it, so removing it is pure dead-code cleanup with zero behavior change.

**Acceptance criteria:**
- `CLASSIFICATION_QUERY_KEYS` no longer contains a `statistics` property.
- No other file in `frontend/src` references `CLASSIFICATION_QUERY_KEYS.statistics`.
- `npm run build` and `npm run lint` pass with no new errors.

### FR-6: Verify no residual references
After FR-1 through FR-5, run a repo-wide search (backend and frontend) for `GetStatisticsAsync`, `ClassificationStatistics`, `RuleUsageStatistic`, `ClassificationStatisticsDto`, `RuleUsageStatisticDto` to confirm zero remaining matches (aside from this spec/brief/history in `artifacts/` or git history, which are not code).

**Acceptance criteria:**
- Grep for each of the five identifiers above returns no matches under `backend/src`, `backend/test`, or `frontend/src`.
- `dotnet build` succeeds with no warnings about unused usings introduced by the deletions (clean up any now-unused `using` statements in touched files, e.g. `InvoiceClassificationMappingProfile.cs` if it no longer needs a using it previously needed only for these types — in this case it doesn't, since `ClassificationStatistics`/`RuleUsageStatistic` live in the same `Anela.Heblo.Domain.Features.InvoiceClassification` namespace already used by other mappings).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this change removes unreachable code and has no runtime performance impact (the removed method was never invoked in production).

### NFR-2: Security
Not applicable — no auth, data exposure, or security-sensitive surface is affected. No new endpoint is added; an existing but unreachable data-aggregation code path is removed.

## Data Model
No data model changes. `ClassificationHistory` (the underlying EF entity, `_context.ClassificationHistory`) is untouched — only unused aggregation/DTO types built on top of it are removed. No database migration is required.

## API / Interface Design
No API changes. No controller endpoint exists today for statistics and none is being added. The public `IClassificationHistoryRepository` interface is unchanged (it never exposed this capability). This is a pure internal cleanup of the concrete repository implementation and unused supporting types.

## Dependencies
None. This change is self-contained within the `InvoiceClassification` module (Domain, Application/Contracts, Application mapping profile, Persistence, and one frontend hooks file) and does not depend on any external service, library, or other in-flight feature.

## Out of Scope
- Building an actual classification-statistics feature (use case handler, `GET /api/invoice-classification/statistics` controller endpoint, frontend hook/UI). If there is future product demand for this, it should go through normal brainstorming/spec process with real requirements (what date ranges, what breakdown, where in the UI it's surfaced, whether it needs a dashboard tile per the existing `invoiceimportstatistics` tile pattern in `frontend/src/components/dashboard/tiles/tileRegistry.tsx`).
- Any change to `GetPagedHistoryAsync`, `GetHistoryAsync`, `GetHistoryByInvoiceIdAsync`, or `AddAsync` — these are actively used and out of scope for this cleanup.
- Any change to the `ClassificationHistory` EF entity or its database table/migrations.
- Any change to other modules' statistics features (e.g., `InvoiceImportStatistics`, `Analytics/invoice-import-statistics`) — these are unrelated, already-implemented features that happen to share the word "statistics" but live in a different module (Automation/Analytics, not InvoiceClassification).

## Open Questions
None.

## Status: COMPLETE
