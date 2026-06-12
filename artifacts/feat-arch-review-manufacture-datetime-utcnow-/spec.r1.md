# Specification: Consistent TimeProvider Usage in Manufacture Order Handlers

## Summary
Three Manufacture handlers inject `TimeProvider` and use it for some timestamp fields while falling back to `DateTime.UtcNow` for others within the same method. This spec defines the surgical replacement of five `DateTime.UtcNow` occurrences with `_timeProvider.GetUtcNow().DateTime` so that all timestamps are controllable by `FakeTimeProvider` in tests.

## Background
The Manufacture module follows a clean-architecture pattern where handlers depend on `TimeProvider` (already registered in DI) so that time-sensitive logic can be deterministically tested. A daily architecture review on 2026-06-06 flagged that three handlers correctly use `_timeProvider` for some assignments but then call `DateTime.UtcNow` directly for others in the same method body. Tests using `FakeTimeProvider` therefore observe a partially frozen clock: expiration, lot, and ERP-date fields are controlled, but `CreatedDate`, `StateChangedAt`, and note `CreatedAt` reflect real wall-clock time. The inconsistency creates silent holes in time-sensitive assertions without compile-time or runtime warning.

## Functional Requirements

### FR-1: Replace direct UtcNow usage in CreateManufactureOrderHandler
Replace `DateTime.UtcNow` with `_timeProvider.GetUtcNow().DateTime` at lines 46 (`CreatedDate`) and 52 (`StateChangedAt`) in `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs`.

**Acceptance criteria:**
- No occurrence of `DateTime.UtcNow` remains in the handler.
- `CreatedDate` and `StateChangedAt` on a newly created `ManufactureOrder` equal the value returned by the injected `TimeProvider` at handler execution time.
- A test using `FakeTimeProvider` with a fixed timestamp observes that timestamp in both fields.

### FR-2: Replace direct UtcNow usage in DuplicateManufactureOrderHandler
Replace `DateTime.UtcNow` with `_timeProvider.GetUtcNow().DateTime` at lines 47 (`CreatedDate`) and 52 (`StateChangedAt`) in `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs`.

**Acceptance criteria:**
- No occurrence of `DateTime.UtcNow` remains in the handler.
- `CreatedDate` and `StateChangedAt` on a duplicated `ManufactureOrder` reflect the injected `TimeProvider` value.
- Test with `FakeTimeProvider` confirms both fields match the frozen time.

### FR-3: Replace direct UtcNow usage in UpdateManufactureOrderHandler
Replace `DateTime.UtcNow` with `_timeProvider.GetUtcNow().DateTime` at line 145 (note `CreatedAt`) in `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/UpdateManufactureOrderHandler.cs`.

**Acceptance criteria:**
- No occurrence of `DateTime.UtcNow` remains in the handler.
- The `CreatedAt` field of a newly appended note reflects the injected `TimeProvider` value.
- Test with `FakeTimeProvider` confirms note `CreatedAt` matches the frozen time.

### FR-4: Test coverage for time-frozen assertions
Add or update unit tests that pass a `FakeTimeProvider` with a deterministic timestamp into each of the three handlers and assert that `CreatedDate`, `StateChangedAt`, and note `CreatedAt` equal that timestamp.

**Acceptance criteria:**
- At least one test per handler asserts the frozen timestamp on each previously-uncontrolled field.
- Tests fail if a future refactor reintroduces `DateTime.UtcNow` in any of these fields.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact. `TimeProvider.GetUtcNow()` is a virtual call already invoked elsewhere in each handler.

### NFR-2: Security
No security implications. No new dependencies, no new exposed surface.

### NFR-3: Build & Format
`dotnet build` and `dotnet format` must pass without warnings or formatting changes outside the touched lines.

### NFR-4: Backwards Compatibility
The change is purely internal to handlers. No DTO, API contract, database schema, or DI registration is modified. Production behavior is identical when the registered `TimeProvider` is `TimeProvider.System` (default).

## Data Model
No data model changes. The affected entity properties are:
- `ManufactureOrder.CreatedDate` (DateTime)
- `ManufactureOrder.StateChangedAt` (DateTime)
- `ManufactureOrderNote.CreatedAt` (DateTime)

All remain `DateTime` typed; only the *source* of the value changes.

## API / Interface Design
No public API, DTO, contract, or UI changes. The three MediatR commands (`CreateManufactureOrderCommand`, `DuplicateManufactureOrderCommand`, `UpdateManufactureOrderCommand`) and their response shapes are unchanged.

## Dependencies
- `Microsoft.Extensions.TimeProvider.Testing` (for `FakeTimeProvider` in tests — already in use elsewhere in the suite).
- `TimeProvider` registration in DI (already present; no change needed).

## Out of Scope
- Any audit of other modules (Catalog, Logistics, Purchase, etc.) for similar `DateTime.UtcNow` leaks. A separate sweep should be filed if desired.
- Refactoring or extracting a helper method around `_timeProvider.GetUtcNow().DateTime`. Direct inline replacement matches existing handler style.
- Changing the property types from `DateTime` to `DateTimeOffset`.
- Adding a Roslyn analyzer or test rule to forbid `DateTime.UtcNow` project-wide.
- Modifying the `ManufactureOrder` or `ManufactureOrderNote` domain entities.

## Open Questions
None.

## Status: COMPLETE