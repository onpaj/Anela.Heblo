# Specification: Deduplicate the "critical error" business rule in IssuedInvoice

## Summary
The definition of a "critical" invoice sync error — currently "any non-null `ErrorType` other than `InvoicePaired`" — is implemented twice: once as a computed property on the `IssuedInvoice` domain entity, and once as a hand-written LINQ predicate inside `IssuedInvoiceRepository.GetSyncStatsAsync`. This spec consolidates the rule into a single, shared definition that both call sites use, following a pattern already established elsewhere in this codebase (`TransportBox`), so the rule can only change in one place.

## Background
`IssuedInvoiceRepository.GetSyncStatsAsync` computes aggregate sync statistics (used by the dashboard/stats view) directly in SQL via `CountAsync`, because EF Core cannot translate a C# computed property (`IssuedInvoice.IsCriticalError`) into SQL. To work around this, the repository re-implements the same boolean predicate as an inline lambda (`IssuedInvoiceRepository.cs:43`). The entity's own `IsCriticalError` property (`IssuedInvoice.cs:53`) is used elsewhere (e.g. mapped to `IssuedInvoiceDto.IsCriticalError` via `InvoicesMappingProfile`, used for the per-invoice detail/list views).

Because the two definitions are textually independent, a future change to what counts as "critical" (e.g. adding a new `IssuedInvoiceErrorType` value that should also be excluded, such as a hypothetical `DuplicateSkipped`) requires remembering to update both places. If only one is updated, the per-invoice badge (entity property) and the aggregate stats dashboard (repository query) will silently disagree — no compiler error, no failing test, just two views of the data showing inconsistent numbers. This is a pure internal consistency/maintainability fix; it does not change any currently observable behavior.

The codebase already has a proven solution to this exact class of problem: `TransportBox` (`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs:39-50`) defines each business predicate once as a `static Expression<Func<TransportBox, bool>>`, compiles it once into a `static Func<TransportBox, bool>`, and exposes a computed property that calls the compiled delegate:

```csharp
public static Expression<Func<TransportBox, bool>> IsInTransportPredicate =
    b => b.State == TransportBoxState.InTransit || b.State == TransportBoxState.Received || b.State == TransportBoxState.Opened;
public static Func<TransportBox, bool> IsInTransportFunc = IsInTransportPredicate.Compile();
public bool IsInTransit => IsInTransportFunc(this);
```

This spec applies the same pattern to `IssuedInvoice.IsCriticalError`, both for consistency with existing conventions and because it is already known to work with this project's EF Core/PostgreSQL setup (the `Expression<Func<T,bool>>` form is directly usable inside `.Where()`/`.CountAsync()` predicates, and translates to SQL).

## Functional Requirements

### FR-1: Single shared definition of the "critical error" predicate
Define the critical-error rule exactly once, as a `static Expression<Func<IssuedInvoice, bool>>` on the `IssuedInvoice` entity (mirroring `TransportBox.IsInTransportPredicate`), named `IsCriticalErrorPredicate`:

```csharp
public static Expression<Func<IssuedInvoice, bool>> IsCriticalErrorPredicate =
    x => x.ErrorType != null && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired;
public static Func<IssuedInvoice, bool> IsCriticalErrorFunc = IsCriticalErrorPredicate.Compile();
```

**Acceptance criteria:**
- Exactly one C# expression in the codebase encodes the "critical error" business rule (the `Expression<Func<IssuedInvoice, bool>>` literal). No other file contains an independently written equivalent boolean condition.
- The predicate's logic is unchanged from today: `ErrorType.HasValue && ErrorType != IssuedInvoiceErrorType.InvoicePaired`.

### FR-2: Entity computed property delegates to the shared predicate
Replace the current `IsCriticalError` computed property body so it evaluates via the compiled delegate rather than restating the condition:

```csharp
public bool IsCriticalError => IsCriticalErrorFunc(this);
```

**Acceptance criteria:**
- `IssuedInvoice.IsCriticalError` returns identical results to the current implementation for all `ErrorType` values (`null`, `General`, `InvoicePaired`, `ProductNotFound`).
- No consumer of `IsCriticalError` (`InvoicesMappingProfile`, `IssuedInvoiceDto`, any other reader) requires code changes — the property's public signature and semantics are unchanged.

### FR-3: Repository stats query uses the shared predicate
Replace the inline lambda in `IssuedInvoiceRepository.GetSyncStatsAsync` (`IssuedInvoiceRepository.cs:43`) with the shared expression:

```csharp
var criticalErrors = await query.CountAsync(IssuedInvoice.IsCriticalErrorPredicate, cancellationToken);
```

**Acceptance criteria:**
- `GetSyncStatsAsync` still translates to a single SQL `COUNT` (no client-side evaluation, no `EF.Functions` warnings, no `AsEnumerable()`/`ToList()` inserted before the count).
- `IssuedInvoiceSyncStats.CriticalErrors` returns the same value as before this change for the same underlying data, for both PostgreSQL (real DB / integration tests, if any) and any in-memory provider used in unit tests.
- Existing test `IssuedInvoiceRepositoryTests` (`backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs:156`, `Assert.Equal(1, stats.CriticalErrors)`) continues to pass unmodified.

### FR-4: Regression test proving the two call sites cannot diverge
Add a unit test (in the existing `Anela.Heblo.Tests` invoice test suite) that, for every value of `IssuedInvoiceErrorType` (including `null`), asserts `IssuedInvoice.IsCriticalError` (entity property) agrees with the result of counting via `IssuedInvoice.IsCriticalErrorPredicate` (or, if a DB-backed fixture is available, with `GetSyncStatsAsync`'s `CriticalErrors` count). This test is the safety net referenced in the finding: it fails loudly if someone later reintroduces a second, divergent definition instead of extending the shared one.

**Acceptance criteria:**
- Test enumerates all values of `IssuedInvoiceErrorType` (`General`, `InvoicePaired`, `ProductNotFound`) plus the `null` case.
- Test fails if `IsCriticalError` and `IsCriticalErrorPredicate` (compiled) ever disagree for any of those values.
- Test is placed alongside existing invoice tests, following existing test naming/structure conventions in `IssuedInvoiceRepositoryTests.cs` or a new `IssuedInvoiceTests.cs` if entity-only unit tests don't yet have a home.

## Non-Functional Requirements

### NFR-1: Performance
No regression: the stats query must remain a single server-side `COUNT` translated to SQL, not client-evaluated. This is the reason the `Expression<Func<...>>` form (not just a `Func<...>`) is required for the field used inside `CountAsync`/`Where`.

### NFR-2: Behavior compatibility
Zero observable behavior change for any existing API response, DTO, or UI. This is a refactor of internal implementation only — `GetIssuedInvoiceSyncStatsHandler`, `IssuedInvoiceDto.IsCriticalError`, and all downstream consumers must produce byte-identical results before and after the change, for identical input data.

## Data Model
No schema, entity shape, or DTO changes. `IssuedInvoiceErrorType` enum (`General`, `InvoicePaired`, `ProductNotFound`) is unchanged. `IssuedInvoiceSyncStats.CriticalErrors` (int) is unchanged. This is a pure internal refactor of how an existing boolean is computed, not a data model change.

## API / Interface Design
No public API, controller, or route changes. Internal surface changes:
- `IssuedInvoice` gains two new `public static` members: `IsCriticalErrorPredicate` (`Expression<Func<IssuedInvoice, bool>>`) and `IsCriticalErrorFunc` (`Func<IssuedInvoice, bool>`), following the naming convention of `TransportBox.IsInTransportPredicate` / `IsInTransportFunc`.
- `IssuedInvoice.IsCriticalError` property signature (`public bool IsCriticalError`) is unchanged.
- `IIssuedInvoiceRepository` interface is unchanged (no new/changed method signatures) — this is purely an implementation-body change inside `IssuedInvoiceRepository.GetSyncStatsAsync`.

## Dependencies
None. No new packages. Relies only on `System.Linq.Expressions`, already used in this codebase (`TransportBox.cs`, `ITransportBoxRepository.cs`, `TransportBoxRepository.cs`).

## Out of Scope
- Reviewing or deduplicating any other business rules in the Invoices module or elsewhere (this spec covers only the `IsCriticalError` finding).
- Adding new `IssuedInvoiceErrorType` values or changing which error types are considered critical.
- Changing the `IssuedInvoiceSyncStats` DTO shape or the stats API response.
- Changing UI rendering of error badges or the stats dashboard.
- Broader introduction of a "specification pattern" abstraction beyond the `Expression`+`Func` convention already used by `TransportBox` — stay consistent with the existing convention rather than introducing a new one.

## Open Questions
None.

## Status: COMPLETE
