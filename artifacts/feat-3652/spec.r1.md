# Specification: Remove Analytics dependency from Bank's `IBankStatementImportRepository`

## Summary
`IBankStatementImportRepository`, a Domain-layer repository interface owned by the Bank module, currently imports and references two Analytics-module types (`DailyBankStatementStatistics`, `BankStatementDateType`) via its `GetDailyStatisticsAsync` method. This violates the module-boundary rule that a module's Domain layer must not depend on another module's types. This spec replaces that method with a Bank-owned `GetDailyCountsAsync` method returning a new Bank-owned `BankDailyCount` record, and moves the projection into Analytics-facing types to `BankStatementStatisticsSourceAdapter`, which is the correct location for cross-module translation.

## Background
The Bank module exposes bank-statement import data to the Analytics module through the adapter pattern: `BankStatementStatisticsSourceAdapter` implements the Analytics-owned contract `IBankStatementStatisticsSource` and internally calls into Bank's repository. When `GetDailyStatisticsAsync` was added to service this adapter, it was added directly to `IBankStatementImportRepository` using Analytics' own return type (`DailyBankStatementStatistics`) and Analytics' own enum (`BankStatementDateType`), requiring `IBankStatementImportRepository.cs` to `using Anela.Heblo.Domain.Features.Analytics`.

This breaks the project's module-boundary rule ("No direct access to another module's entities"; "Communication between modules exclusively through contracts/interfaces") at the Domain layer — the strictest layer, where it applies to repository interfaces exactly as it does to entities. Concretely, Bank's Domain layer currently cannot compile, be tested, or be deployed independently of Analytics, and any shape change to `DailyBankStatementStatistics` or `BankStatementDateType` breaks Bank's domain interface even though Bank has no business-level dependency on Analytics.

This is a pure architecture-boundary refactor filed by the daily arch-review routine (issue #3652). It has verified reconnaissance (current signatures, all call sites, all test dependents) already documented in the brief; no exploratory investigation is required before implementation. Option A (Bank-typed query method, adapter does the projection) is the preferred and specified approach; Option B is documented only as a rejected alternative.

## Functional Requirements

### FR-1: Introduce a Bank-owned `BankDailyCount` record
Add a new record type to the Bank Domain layer (`backend/src/Anela.Heblo.Domain/Features/Bank/`) that carries raw per-day statement counts without any dependency on Analytics types.

```csharp
public record BankDailyCount(DateTime Date, int ImportCount, int TotalItemCount);
```

This is a same-shape, Bank-owned mirror of the existing Analytics type `DailyBankStatementStatistics` (which has exactly the fields `Date`, `ImportCount`, `TotalItemCount`) — it is not a new business concept, it exists solely to keep the Bank Domain layer type-independent from Analytics.

**Acceptance criteria:**
- `BankDailyCount` is defined in `Anela.Heblo.Domain.Features.Bank` (or an equivalent Bank-owned namespace within Domain/Features/Bank), not in Analytics.
- `BankDailyCount` has fields `Date` (`DateTime`), `ImportCount` (`int`), `TotalItemCount` (`int`).
- No file under `Domain/Features/Bank/` that references `BankDailyCount` needs to import `Anela.Heblo.Domain.Features.Analytics`.

### FR-2: Replace `GetDailyStatisticsAsync` with `GetDailyCountsAsync` on `IBankStatementImportRepository`
In `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs`, remove the `GetDailyStatisticsAsync` method (which currently takes `BankStatementDateType` and returns `IReadOnlyList<DailyBankStatementStatistics>`) and replace it with a Bank-typed equivalent:

```csharp
Task<IReadOnlyList<BankDailyCount>> GetDailyCountsAsync(
    DateTime startDate,
    DateTime endDate,
    bool byStatementDate,
    CancellationToken cancellationToken = default);
```

The `bool byStatementDate` parameter replaces the Analytics enum `BankStatementDateType`: `true` corresponds to the previous `BankStatementDateType.StatementDate` behavior (group/filter by statement date), `false` corresponds to `BankStatementDateType.ImportDate` behavior (group/filter by import date).

Remove the `using Anela.Heblo.Domain.Features.Analytics;` import from this file once `BankStatementDateType` and `DailyBankStatementStatistics` are no longer referenced in it.

**Acceptance criteria:**
- `IBankStatementImportRepository` no longer declares `GetDailyStatisticsAsync`.
- `IBankStatementImportRepository` declares `GetDailyCountsAsync` with the signature above (or an equivalent Bank-owned signature carrying the same semantic information: date range + a Bank-owned/boolean date-type selector).
- `IBankStatementImportRepository.cs` contains no `using Anela.Heblo.Domain.Features.Analytics;` and no reference to `DailyBankStatementStatistics` or `BankStatementDateType`.

### FR-3: Update the EF Core implementation
In `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` (currently implementing `GetDailyStatisticsAsync` at lines 143-195), rename/retype the method to match the new interface:
- Accept `bool byStatementDate` instead of `BankStatementDateType dateType`; branch on the bool where the old code switched on the enum (`StatementDate` vs `ImportDate`) to choose which date column to group by.
- Keep the existing grouping logic: group `_context.BankStatements` by Year/Month/Day of the selected date column, project `ImportCount` (count of rows) and `TotalItemCount` (`Sum(ItemCount)`) per day.
- Return `IReadOnlyList<BankDailyCount>` instead of constructing `DailyBankStatementStatistics` — the projection to the Analytics type moves to the adapter (FR-4). This method must not construct or reference `DailyBankStatementStatistics`.
- Remove the `using Anela.Heblo.Domain.Features.Analytics;` import from this file once no longer needed.

**Acceptance criteria:**
- `BankStatementImportRepository.GetDailyCountsAsync` compiles against the new interface signature and returns `BankDailyCount` rows with correct `Date`, `ImportCount`, `TotalItemCount` values, using the same grouping semantics (per calendar day) as before.
- The `byStatementDate` bool correctly selects between statement-date and import-date grouping, preserving prior behavior for both `BankStatementDateType.StatementDate` and `.ImportDate` cases.
- No `DailyBankStatementStatistics` or `BankStatementDateType` reference remains in this file.

### FR-4: Update `BankStatementStatisticsSourceAdapter` to project Bank types to Analytics types
`backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs` continues to implement the unchanged Analytics-owned contract `IBankStatementStatisticsSource.GetDailyStatisticsAsync` (defined in `Domain/Features/Analytics/IBankStatementStatisticsSource.cs` — this interface and its method signature are out of scope and must not change). Update the adapter's internal implementation:

1. Call `_repository.GetDailyCountsAsync(startDate, endDate, dateType == BankStatementDateType.StatementDate, cancellationToken)` instead of the old `GetDailyStatisticsAsync` call.
2. Map each returned `BankDailyCount` to a `DailyBankStatementStatistics { Date, ImportCount, TotalItemCount }` instance.
3. Preserve the existing gap-fill behavior unchanged: every date in `[startDate, endDate]` that is absent from the repository result must be filled with a zero-count `DailyBankStatementStatistics` row, exactly as today.

The adapter's own public method name and signature (`GetDailyStatisticsAsync`, satisfying `IBankStatementStatisticsSource`) do not change — only its internal call site and mapping logic change.

**Acceptance criteria:**
- `BankStatementStatisticsSourceAdapter` still implements `IBankStatementStatisticsSource` with an unchanged public signature.
- For any given `startDate`, `endDate`, `dateType`, the adapter's output (`IReadOnlyList<DailyBankStatementStatistics>`, including gap-filled zero rows for missing dates) is byte-for-byte equivalent, field-for-field, to its pre-refactor output.
- `test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs` passes unmodified (no test code changes required in this file), since it only calls the adapter's unchanged public method against a real EF Core in-memory repository.

## Non-Functional Requirements

### NFR-1: Performance
No performance change is expected or required. The refactor changes types and a projection step only; the underlying EF Core query (grouping, filtering, aggregation) is logically unchanged. No new database round-trips are introduced. This is not a performance-sensitive change and no specific timing target applies beyond "no regression."

### NFR-2: Security
Not applicable. No new data exposure, authentication, or authorization surface is introduced; this is an internal type/contract refactor with no externally observable behavior change.

### NFR-3: Architectural integrity (primary driver for this change)
- `Anela.Heblo.Domain.Features.Bank` must have zero compile-time or reference-time dependency on `Anela.Heblo.Domain.Features.Analytics` after this change (verifiable via a project/namespace reference check or a `using` grep).
- The Analytics-owned contract `IBankStatementStatisticsSource` (and its method `GetDailyStatisticsAsync`) must remain byte-for-byte unchanged — it is the correct, already-compliant integration point and must not be touched.
- The cross-module adapter pattern (`BankStatementStatisticsSourceAdapter` implementing an Analytics-owned interface while consuming Bank-owned types) must be preserved as the mechanism for cross-module communication; this refactor strengthens rather than removes that pattern.

## Data Model

**New type — `BankDailyCount`** (Domain/Features/Bank, Bank-owned):
| Field | Type | Description |
|---|---|---|
| `Date` | `DateTime` | Calendar day the count applies to |
| `ImportCount` | `int` | Number of bank statement import rows on that day |
| `TotalItemCount` | `int` | Sum of `ItemCount` across those rows |

**Unchanged type — `DailyBankStatementStatistics`** (Domain/Features/Analytics, Analytics-owned): same three fields (`Date`, `ImportCount`, `TotalItemCount`); remains the Analytics module's own representation, now populated by the adapter's mapping from `BankDailyCount` rather than being constructed inside the Bank repository.

**Relationship:** `BankDailyCount` and `DailyBankStatementStatistics` are structurally identical but semantically and ownership-wise distinct — the former is Bank's internal representation, the latter is Analytics' contract type. The adapter (`BankStatementStatisticsSourceAdapter`) is the sole translation point between them. No persistence/schema changes — both types are in-memory query-result DTOs, not EF entities; no database migration is required.

## API / Interface Design

**Removed (Domain/Features/Bank/IBankStatementImportRepository.cs):**
```csharp
Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
    DateTime startDate, DateTime endDate, BankStatementDateType dateType,
    CancellationToken cancellationToken = default);
```

**Added (Domain/Features/Bank/IBankStatementImportRepository.cs):**
```csharp
Task<IReadOnlyList<BankDailyCount>> GetDailyCountsAsync(
    DateTime startDate, DateTime endDate, bool byStatementDate,
    CancellationToken cancellationToken = default);
```

**Unchanged (Domain/Features/Analytics/IBankStatementStatisticsSource.cs) — out of scope, must not change:**
```csharp
Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
    DateTime startDate, DateTime endDate, BankStatementDateType dateType,
    CancellationToken cancellationToken = default);
```

**Call graph after the change:**
`IBankStatementStatisticsSource.GetDailyStatisticsAsync` (Analytics contract, called by `AnalyticsRepository`)
→ `BankStatementStatisticsSourceAdapter.GetDailyStatisticsAsync` (implements the above; maps `dateType` → `bool`, maps `BankDailyCount` → `DailyBankStatementStatistics`, gap-fills)
→ `IBankStatementImportRepository.GetDailyCountsAsync` (Bank contract, Analytics-agnostic)
→ `BankStatementImportRepository.GetDailyCountsAsync` (EF Core implementation).

No HTTP/REST endpoints, no MediatR handlers, and no frontend surface are affected — this is entirely internal, backend-only, below the application/controller layer.

## Dependencies
- No new external libraries or services.
- Depends on the existing EF Core `DbContext`/`BankStatements` table already used by `BankStatementImportRepository`.
- Depends on the Analytics module's `IBankStatementStatisticsSource` and `DailyBankStatementStatistics`/`BankStatementDateType` remaining stable (they are explicitly out of scope and unchanged by this work).

## Out of Scope
- Any change to `Domain/Features/Analytics/IBankStatementStatisticsSource.cs` or its method signature.
- Any change to `Persistence/Features/Analytics/AnalyticsRepository.cs` (it calls the unchanged Analytics-owned interface method and is unaffected).
- Any change to `test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs` (must keep passing unmodified).
- Any change to `ImportBankStatementHandlerTests.cs`, `GetBankStatementListHandlerTests.cs`, `Infrastructure/Jobs/*Tests.cs`, `GetBankStatementByIdHandlerTests.cs` — verified by grep to reference `IBankStatementImportRepository` only for other, unrelated methods.
- Option B (moving the query to an Application-layer-only internal repository, e.g. `IBankDailyStatisticsQuery`) — documented in the brief as an alternative but not selected; not to be implemented.
- Any behavioral change to the statistics themselves (grouping logic, gap-fill semantics, date range inclusivity) — this is a pure type/layering refactor with identical externally observable output from the adapter.
- Any frontend, API contract (OpenAPI), or MediatR/controller changes — none of those layers touch this repository interface.
- Database schema/migration changes — none required, as both old and new types are transient query DTOs.

## Open Questions
None.

## Status: COMPLETE
