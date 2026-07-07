# Specification: Remove application-layer concern from `DailyInvoiceCount` Domain type

## Summary
`DailyInvoiceCount.IsBelowThreshold`, a Domain-layer property, is currently written by an Application-layer handler based on a configuration value (`InvoiceImportOptions.MinimumDailyThreshold`), and is also unconditionally set to `false` by an infrastructure-facing adapter. This spec removes `IsBelowThreshold` from the Domain type and introduces a new `DailyInvoiceCountDto` class in the Analytics module's `Contracts/` folder that carries the computed threshold flag, keeping the Domain type immutable and free of application concerns. Response DTO shape and behavior observed by API consumers remain unchanged.

## Background
The Analytics module's `GetInvoiceImportStatisticsHandler` retrieves `DailyInvoiceCount` domain objects from `IAnalyticsRepository.GetInvoiceImportStatisticsAsync` and then mutates each returned object, setting `IsBelowThreshold = dayCount.Count < minimumThreshold`. `minimumThreshold` comes from `InvoiceImportOptions`, an Application-layer configuration concern. This violates Clean Architecture: the Domain type's meaning depends on knowledge the Domain layer should not have, and the Domain object is mutated after retrieval by an Application service.

A parallel Consumer-Owned Contract, `IInvoiceImportStatisticsSource` (implemented by `InvoiceImportStatisticsSourceAdapter` in the Invoices module), already documents this smell in its own XML doc comment: `IsBelowThreshold` is always `false` from that source because "the consumer decides thresholds." That adapter is not currently wired into `GetInvoiceImportStatisticsHandler` (which uses `IAnalyticsRepository` instead) — it exists but sets the field to a permanent dummy value, which is itself evidence the property does not belong on the Domain type.

This is a small, contained architecture-review remediation. No new user-facing functionality is introduced; the goal is to relocate the threshold computation to the correct layer while keeping the API response shape and computed values byte-for-byte identical.

## Functional Requirements

### FR-1: Remove `IsBelowThreshold` from the Domain type and compute it in a new Application-layer DTO
Remove the `IsBelowThreshold` property from `Anela.Heblo.Domain.Features.Analytics.DailyInvoiceCount` (`backend/src/Anela.Heblo.Domain/Features/Analytics/DailyInvoiceCount.cs`). The Domain type retains only `Date` and `Count`.

Add a new class `DailyInvoiceCountDto` to `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/DailyInvoiceCountDto.cs`, following the existing convention of other DTOs in that folder (e.g. `TopProductDto.cs`): a plain C# **class** (never a record, per repo convention), with `Date`, `Count`, and `IsBelowThreshold` properties.

Update `GetInvoiceImportStatisticsHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs`) to:
- Stop mutating the `DailyInvoiceCount` objects returned by the repository.
- Project each `DailyInvoiceCount` into a `DailyInvoiceCountDto`, computing `IsBelowThreshold = dayCount.Count < minimumThreshold` at projection time, matching the existing `<` comparison semantics exactly.

Update `GetInvoiceImportStatisticsResponse` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsResponse.cs`) so `Data` is typed `List<DailyInvoiceCountDto>` instead of `List<DailyInvoiceCount>`.

**Acceptance criteria:**
- `DailyInvoiceCount` (Domain) no longer has an `IsBelowThreshold` property or setter.
- `DailyInvoiceCount` (Domain) remains otherwise unchanged (`Date`, `Count`).
- `DailyInvoiceCountDto` exists in `Anela.Heblo.Application.Features.Analytics.Contracts`, is a class (not a record), and has `Date` (DateTime), `Count` (int), `IsBelowThreshold` (bool) properties with public getters/setters, matching the existing DTO style in that folder.
- `GetInvoiceImportStatisticsResponse.Data` is `List<DailyInvoiceCountDto>`.
- `GetInvoiceImportStatisticsHandler` no longer mutates objects returned from `_analyticsRepository.GetInvoiceImportStatisticsAsync`; it maps them to `DailyInvoiceCountDto` and computes `IsBelowThreshold` during that mapping.
- For a given `Count` and `MinimumThreshold`, the computed `IsBelowThreshold` value is identical to the value produced by the current implementation (`Count < MinimumThreshold`).
- The serialized JSON shape of `GET` invoice import statistics responses (field names `date`, `count`, `isBelowThreshold`, `minimumThreshold`) is unchanged — only the underlying C# type backing the `data` array changes.
- Solution builds cleanly (`dotnet build`) with no remaining references to `DailyInvoiceCount.IsBelowThreshold`.

### FR-2: Update all call sites that reference `DailyInvoiceCount.IsBelowThreshold` (backend)
The following backend call sites currently construct `DailyInvoiceCount` with `IsBelowThreshold` set (always to `false`, since only the handler computed a real value) and must be updated to stop doing so, since the property will no longer exist on the Domain type:

- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs` — three call sites construct `new DailyInvoiceCount { ..., IsBelowThreshold = false }` (lines ~47-52, ~71-76, ~92-97 per current file). Remove the `IsBelowThreshold = false` initializer from all three. Update the XML doc comment on `IInvoiceImportStatisticsSource.GetDailyCountsAsync` (`backend/src/Anela.Heblo.Domain/Features/Analytics/IInvoiceImportStatisticsSource.cs`) to remove the now-inapplicable sentence about `IsBelowThreshold` always being `false`.

**Acceptance criteria:**
- `InvoiceImportStatisticsSourceAdapter` compiles without setting `IsBelowThreshold` anywhere.
- `IInvoiceImportStatisticsSource`'s XML doc no longer references `IsBelowThreshold`.
- No production code outside the handler (per FR-1) references a threshold flag on the Domain `DailyInvoiceCount` type.

### FR-3: Update backend tests referencing `DailyInvoiceCount.IsBelowThreshold`
The following existing tests construct `DailyInvoiceCount` objects with `IsBelowThreshold` set and must be updated to compile and to keep asserting equivalent behavior against the new DTO:

- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs` — mock repository returns `List<DailyInvoiceCount>` objects that currently include `IsBelowThreshold = false` in their initializers (these must drop that initializer, since the mocked repository return type stays `DailyInvoiceCount` without the property). Assertions on the handler's result (`result.Data[0].IsBelowThreshold`, `result.Data[1].IsBelowThreshold`) continue to work unchanged in spirit, but now assert against `DailyInvoiceCountDto` instances in `result.Data`.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/DashboardTiles/InvoiceImportStatisticsTileTests.cs` — two `DailyInvoiceCount` literals include `IsBelowThreshold = false`; drop that initializer (this test does not assert on the threshold flag, only on `Count`, so removing the initializer is sufficient).
- `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapterTests.cs` — referenced by the earlier grep for `IsBelowThreshold`; inspect and update any assertions or object initializers that reference the removed property so the test file compiles and continues to verify the adapter's actual behavior (gap-filling and grouping), independent of the removed flag.

**Acceptance criteria:**
- All three test files compile without referencing `DailyInvoiceCount.IsBelowThreshold`.
- `GetInvoiceImportStatisticsHandlerTests.Handle_ShouldReturnStatisticsWithMinimumThreshold` still asserts that a day with `Count = 15` and threshold `10` yields `IsBelowThreshold == false`, and a day with `Count = 5` yields `IsBelowThreshold == true`, now read from `DailyInvoiceCountDto` objects in the response.
- No test asserts on a nonexistent `DailyInvoiceCount.IsBelowThreshold` member.
- `dotnet build` and the full existing test suite for the Analytics and Invoices modules pass.

### FR-4: Update frontend generated API client and consumers
The frontend OpenAPI TypeScript client is auto-generated on build (per project convention) and will be regenerated once the backend response DTO changes. Because NSwag (or the project's generator) names the generated TypeScript class after the C# response DTO class, renaming `DailyInvoiceCount` → `DailyInvoiceCountDto` in the Application layer will change the generated TypeScript export name from `DailyInvoiceCount` to `DailyInvoiceCountDto` in `frontend/src/api/generated/api-client.ts`. The generated class's field shape (`date`, `count`, `isBelowThreshold`) stays the same, so no runtime/JSON behavior changes — but every place that imports the type name `DailyInvoiceCount` from the generated client needs to follow the rename:

- `frontend/src/api/hooks/useInvoiceImportStatistics.ts` — imports and re-exports `DailyInvoiceCount` from `../generated/api-client`. Update the import and re-export to `DailyInvoiceCountDto` (or add a local type alias `export type DailyInvoiceCount = DailyInvoiceCountDto` if minimizing downstream churn is preferred — see Open Questions).
- `frontend/src/components/charts/InvoiceImportChart.tsx` — imports `DailyInvoiceCount` from `'../../api/hooks/useInvoiceImportStatistics'` for its `InvoiceImportChartProps.data` type. Update to match whatever name is exported per the previous bullet.
- Re-run frontend codegen (`npm run` script per `docs/development/api-client-generation.md`) as part of the build so `api-client.ts` reflects the new backend type name; do not hand-edit the generated file.

**Acceptance criteria:**
- After backend changes and client regeneration, `npm run build` succeeds with no TypeScript errors related to `DailyInvoiceCount`/`DailyInvoiceCountDto`.
- `npm run lint` passes.
- `InvoiceImportChart.tsx` and `InvoiceImportStatistics.tsx` continue to render the same `isBelowThreshold`-driven behavior (red reference dot, tooltip warning, problematic-day count) with no visual or logic change.
- `frontend/src/api/hooks/__tests__/useInvoiceImportStatistics.test.ts` continues to pass unmodified (it mocks the API client response as a plain object literal with `isBelowThreshold`, so it is not sensitive to the TS class rename, only to the JSON field name, which is unchanged).

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. The change replaces an in-place mutation loop with an equivalent `Select`/projection over the same collection; complexity remains O(n) in the number of days returned (bounded by `DaysBack`, typically ≤ a few hundred).

### NFR-2: Security
No security-relevant surface is touched. No new external inputs, no change to authorization on the `GetInvoiceImportStatistics` endpoint.

## Data Model
- **`DailyInvoiceCount`** (Domain, `Anela.Heblo.Domain.Features.Analytics`) — after this change: `Date` (DateTime), `Count` (int). No longer carries `IsBelowThreshold`.
- **`DailyInvoiceCountDto`** (Application, new, `Anela.Heblo.Application.Features.Analytics.Contracts`) — `Date` (DateTime), `Count` (int), `IsBelowThreshold` (bool). Class, not record.
- **`GetInvoiceImportStatisticsResponse`** (Application) — `Data: List<DailyInvoiceCountDto>`, `MinimumThreshold: int`, plus inherited `BaseResponse` fields (`Success`, etc.). No change to `MinimumThreshold` or `BaseResponse` members.

## API / Interface Design
No change to the public HTTP contract. `GET` invoice import statistics endpoint (backed by `GetInvoiceImportStatisticsRequest`/`Handler`) continues to return JSON of the shape:

```json
{
  "data": [
    { "date": "2026-06-20T00:00:00Z", "count": 15, "isBelowThreshold": false },
    { "date": "2026-06-21T00:00:00Z", "count": 5, "isBelowThreshold": true }
  ],
  "minimumThreshold": 10,
  "success": true
}
```

The only visible change is the OpenAPI schema component name for the array item type, from `DailyInvoiceCount` to `DailyInvoiceCountDto`, which flows into the generated TypeScript client's exported class/interface name (see FR-4).

## Dependencies
- Existing `InvoiceImportOptions` configuration (`MinimumDailyThreshold`) — unchanged, still read by the handler.
- Frontend OpenAPI client codegen tooling (per `docs/development/api-client-generation.md`) — must be re-run so `api-client.ts` picks up the renamed schema.
- No new external services or libraries.

## Out of Scope
- Wiring `IInvoiceImportStatisticsSource`/`InvoiceImportStatisticsSourceAdapter` into `GetInvoiceImportStatisticsHandler` in place of `IAnalyticsRepository` — that is a separate, larger refactor (tracked by the existing plan doc `docs/superpowers/plans/2026-06-04-decouple-analytics-repository-from-invoices-and-bank.md`) and not part of this fix.
- Any change to the `MinimumDailyThreshold` comparison semantics (`<` vs `<=`) — preserved exactly as-is.
- Any change to the bank statement import statistics feature (`DailyBankStatementStatistics`, `GetBankStatementImportStatisticsHandler`), which has a structurally similar pattern but is not mentioned in the arch-review finding and is left untouched.
- Any UI/UX changes to the invoice import statistics chart or page beyond the mechanical type-name follow-through described in FR-4.

## Open Questions
- FR-4 proposes two options for the frontend type rename: (a) propagate the new generated name `DailyInvoiceCountDto` through `useInvoiceImportStatistics.ts` and `InvoiceImportChart.tsx`, or (b) keep a local `DailyInvoiceCount` alias in the hook file so only one file changes. Either is acceptable and low-risk; the implementer should pick whichever keeps the diff smaller and note the choice in the PR. Recommendation: option (a), for consistency with the rest of the generated-client usage pattern in this codebase (no other hook appears to alias a generated type).
- `InvoiceImportStatisticsSourceAdapterTests.cs` was identified via grep as referencing `IsBelowThreshold` but was not opened during spec authoring; the implementer should read it directly before editing to confirm exactly what assertions need updating (see FR-3).

## Status: HAS_QUESTIONS
