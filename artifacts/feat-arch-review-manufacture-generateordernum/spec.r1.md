# Specification: Replace DateTime.Now with caller-supplied year in ManufactureOrderRepository.GenerateOrderNumberAsync

## Summary
The `ManufactureOrderRepository.GenerateOrderNumberAsync` method derives the year prefix for manufacture order numbers from `DateTime.Now` (local server time), which is inconsistent with the rest of the Manufacture module that uses `TimeProvider`/`DateTime.UtcNow`. This spec refactors the method to accept the year as a parameter, sourced by handlers from the injected `TimeProvider`, eliminating the hidden temporal dependency and ensuring order numbers align with audit timestamps on the same row.

## Background
The Manufacture module uses `TimeProvider.GetUtcNow()` everywhere temporal values are stamped — in `CreateManufactureOrderHandler`, `DuplicateManufactureOrderHandler`, and other module handlers. The repository's `GenerateOrderNumberAsync` is the sole site that reads the OS local clock via `DateTime.Now`.

On a server running with a non-UTC offset (e.g. CET = UTC+1), this causes the order number's year prefix to diverge from the order row's `CreatedDate` (which is UTC). The defect window is the year boundary:

- At **23:30 UTC on 31 December**, CET local time is already **00:30 on 1 January**. The repository emits `MO-{next_year}-001` while the handler's `TimeProvider`-driven `CreatedDate` still records the old year.
- At **23:30 CET on 31 December** (= 22:30 UTC), the repository emits `MO-{next_year}` while `CreatedDate` still records the old year — same inconsistency, opposite direction depending on how the host clock is configured.

The bug:
- Breaks audit consistency: order number year ≠ creation year.
- Hides a temporal dependency inside the infrastructure layer, making year-boundary edge cases untestable without manipulating the OS clock.
- Violates module-wide convention that time flows through `TimeProvider`.

Location: `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs:150`.

## Functional Requirements

### FR-1: Repository accepts year as a parameter
`IManufactureOrderRepository.GenerateOrderNumberAsync` must take an `int year` parameter and use it verbatim as the prefix year. The repository must not read any clock (`DateTime.Now`, `DateTime.UtcNow`, `TimeProvider`, etc.) to derive the year.

**Acceptance criteria:**
- The interface signature is `Task<string> GenerateOrderNumberAsync(int year, CancellationToken cancellationToken = default)`.
- The implementation builds the prefix as `$"MO-{year}-"` using the supplied `year` argument.
- A grep for `DateTime.Now`, `DateTime.UtcNow`, and `TimeProvider` inside `ManufactureOrderRepository.GenerateOrderNumberAsync` returns no matches.
- All sequence-lookup and number-formatting behavior downstream of the prefix calculation is unchanged.

### FR-2: Handlers supply the year from TimeProvider
Both call sites — `CreateManufactureOrderHandler` and `DuplicateManufactureOrderHandler` — must compute the year from the injected `TimeProvider` (`_timeProvider.GetUtcNow().Year`) and pass it to the repository.

**Acceptance criteria:**
- `CreateManufactureOrderHandler` calls `_repository.GenerateOrderNumberAsync(_timeProvider.GetUtcNow().Year, cancellationToken)` (or the equivalent via a local variable).
- `DuplicateManufactureOrderHandler` does the same.
- No other call site of `GenerateOrderNumberAsync` exists; if one is discovered during implementation, it is updated identically and listed in the PR description.

### FR-3: Generated order numbers reflect the UTC year
For any order created when the handler's `TimeProvider` UTC instant falls in year Y, the resulting order number prefix is `MO-Y-`.

**Acceptance criteria:**
- A unit test using a fake/mocked `TimeProvider` set to `2026-12-31T23:30:00Z` produces an order number `MO-2026-…` regardless of the host OS time zone.
- A unit test using a fake/mocked `TimeProvider` set to `2027-01-01T00:30:00Z` produces an order number `MO-2027-…`.
- The order's `CreatedDate` (or equivalent UTC timestamp set by the handler) and the year segment of the order number always derive from the same `TimeProvider` reading within a single handler invocation.

### FR-4: Sequence number behavior preserved
The existing sequence-suffix logic (the `-001`, `-002`, … portion) must continue to function: it counts existing orders for the supplied year and returns the next available suffix.

**Acceptance criteria:**
- For year Y with no existing orders, the suffix is `001` (or whatever the current implementation produces — the new code must match the existing format and padding).
- For year Y with N existing orders, the suffix is the next integer formatted identically to today.
- Suffix lookup uses the supplied `year` argument exclusively; no clock is consulted to determine which year's sequence to query.

## Non-Functional Requirements

### NFR-1: Performance
No performance change is expected or acceptable. The refactor removes a clock read and adds an `int` parameter; database query shape and indexing are unchanged.

### NFR-2: Testability
The refactored method must be unit-testable for year-boundary scenarios without manipulating the system clock or the host time zone. Tests inject a fake `TimeProvider` at the handler level and assert on the year prefix produced.

### NFR-3: Backwards compatibility
- Order numbers produced before this change remain valid and queryable — no migration of existing data is required.
- The change is purely additive at the interface level (one new required parameter); no public API surface beyond the repository interface is altered.

### NFR-4: Consistency with module conventions
After this change, **no** code path in the Manufacture module reads the wall clock to stamp data. All temporal values flow through `TimeProvider`. The repository contains no temporal dependency.

## Data Model
No changes. The `ManufactureOrder` entity and its `OrderNumber` column are unchanged. The generated string format `MO-{year}-{sequence}` is preserved.

## API / Interface Design

**Interface (Domain layer):**
```csharp
// IManufactureOrderRepository
Task<string> GenerateOrderNumberAsync(int year, CancellationToken cancellationToken = default);
```

**Implementation (Persistence layer):**
```csharp
public async Task<string> GenerateOrderNumberAsync(int year, CancellationToken cancellationToken = default)
{
    var prefix = $"MO-{year}-";
    // existing sequence-lookup logic, parameterized on `prefix` / `year`
}
```

**Caller pattern (both handlers):**
```csharp
var year = _timeProvider.GetUtcNow().Year;
var orderNumber = await _repository.GenerateOrderNumberAsync(year, cancellationToken);
```

No HTTP, MediatR, or contract DTO changes. No frontend changes.

## Dependencies
- Existing `TimeProvider` registration in DI (already used by Manufacture handlers).
- `IManufactureOrderRepository` interface (Domain layer).
- `ManufactureOrderRepository` implementation (Persistence layer).
- Call sites: `CreateManufactureOrderHandler`, `DuplicateManufactureOrderHandler` (Application layer).
- Existing unit-test project for the Manufacture module.

No new packages, no new infrastructure, no database migration.

## Out of Scope
- Issues #2676–#2679 (other temporal-dependency findings in Manufacture handlers). Those handlers correctly use `_timeProvider.GetUtcNow()` after their own fixes; this spec touches only the repository and the two call sites that pass the year in.
- Changing the order number format (`MO-{year}-{seq}`), sequence padding width, or sequence-rollover rules.
- Backfilling, renumbering, or auditing historical orders whose number prefix and `CreatedDate` already diverge due to the prior bug.
- Introducing a clock abstraction at the repository level (e.g. injecting `TimeProvider` into the repository). The agreed approach is to remove temporal dependency from the repository, not to add one.
- Changing how other Manufacture-module repositories handle time.

## Open Questions
None.

## Status: COMPLETE