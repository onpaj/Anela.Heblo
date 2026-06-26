# Specification: Unit Tests for DuplicateManufactureOrderHandler

## Summary
Add unit-test coverage for `DuplicateManufactureOrderHandler` — the MediatR handler that produces a `Draft` duplicate of an existing manufacture order. The handler currently has zero direct test coverage; controller tests mock the mediator and never execute its body. This work introduces a focused unit-test suite that locks down the handler's branching behavior, quantity-reset semantics, and lot/expiration generation.

## Background
`DuplicateManufactureOrderHandler` lives at `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs`. The Manufacture module has 64 tests total, but this handler is only referenced indirectly from `ManufactureOrderControllerTests.cs`, where the mediator is mocked. Its business logic — source lookup, null guard around `SemiProduct`, quantity reset to planned, and derivation of lot/expiration via `ManufactureOrderExtensions` — is therefore untested.

The risk is silent regression: if the `SemiProduct` null guard is inverted, duplicated orders quietly drop their semi-product definition; if `ActualQuantity` is changed to copy the source's actual instead of the planned value, the production team gets pre-filled (and likely wrong) quantities with no failing test. Filed by the weekly coverage-gap routine on 2026-06-08 against CI run #27104028537.

## Functional Requirements

### FR-1: Test project location and naming
Add a new test class `DuplicateManufactureOrderHandlerTests` in the existing Manufacture test area, mirroring the source layout.

**Acceptance criteria:**
- File created at `backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandlerTests.cs` (matching the existing test-project convention for the module — verify exact path when creating, and align if the module already uses a different folder).
- Class is `public sealed` and uses xUnit `[Fact]` tests with AAA structure.
- Assertions use FluentAssertions; dependencies are mocked with Moq or NSubstitute (whichever the Manufacture test area already uses — match existing style).

### FR-2: Test — source order not found returns `OrderNotFound`
Verify that when `IManufactureOrderRepository.GetOrderByIdAsync` returns `null`, the handler returns a response carrying the `ErrorCodes.OrderNotFound` error and does not attempt to persist anything.

**Acceptance criteria:**
- Arrange: repository mock returns `null` for any `SourceOrderId`.
- Act: invoke `Handle` with a valid request.
- Assert: response's error code equals `ErrorCodes.OrderNotFound`.
- Assert: `GenerateOrderNumberAsync` is **never** called.
- Assert: `AddOrderAsync` is **never** called.

### FR-3: Test — source order with semi-product is fully duplicated
Verify that when the source order has a `SemiProduct` and one or more `Products`, the duplicate is created with: a fresh order number, `Draft` state, current-user attribution, planned date = "today" (from the time provider), `ActualQuantity` reset to `PlannedQuantity` on both semi-product and every product, and lot/expiration values derived from the time provider and the semi-product's `ExpirationMonths`.

**Acceptance criteria:**
- Arrange: build a source `ManufactureOrder` with a populated `SemiProduct` (non-zero `PlannedQuantity`, non-zero `ActualQuantity` distinct from planned, `ExpirationMonths` = e.g. 24) and at least two `Products` (each with distinct planned vs. actual quantities).
- Arrange: `FakeTimeProvider` set to a fixed UTC instant (e.g. `2026-06-08T10:00:00Z`).
- Arrange: `ICurrentUserService.GetCurrentUser()` returns a user whose `GetDisplayName()` returns a known string.
- Arrange: `GenerateOrderNumberAsync` returns a known order number (e.g. `"MO-2026-0042"`).
- Arrange: `AddOrderAsync` captures the passed `ManufactureOrder` and returns it with an `Id` set.
- Assert: response has no error, `Id` and `OrderNumber` reflect the persisted order.
- Assert on the captured `ManufactureOrder`:
  - `OrderNumber` equals the generated order number.
  - `State` equals `ManufactureOrderState.Draft`.
  - `CreatedByUser` and `StateChangedByUser` equal the current user's display name.
  - `ResponsiblePerson` equals the source's `ResponsiblePerson`.
  - `PlannedDate` equals `DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime)`.
  - `SemiProduct` is non-null and has: `ProductCode`, `ProductName`, `PlannedQuantity`, `BatchMultiplier`, `ExpirationMonths` copied from source; `ActualQuantity` equals source `PlannedQuantity` (not source `ActualQuantity`); `LotNumber` equals `ManufactureOrderExtensions.GetDefaultLot(fixedDateTime)`; `ExpirationDate` equals `ManufactureOrderExtensions.GetDefaultExpiration(fixedDateTime, source.SemiProduct.ExpirationMonths)`.
  - `Products.Count` equals source `Products.Count`; for each product, `ProductCode`, `ProductName`, `SemiProductCode`, and `PlannedQuantity` are copied; `ActualQuantity` equals the source's `PlannedQuantity` (not source `ActualQuantity`); `LotNumber` and `ExpirationDate` equal the same values computed for the semi-product.

### FR-4: Test — source order without semi-product attaches no semi-product
Verify that when `sourceOrder.SemiProduct == null`, the duplicate is still produced with its `Products`, no `SemiProduct` is attached, and product `ExpirationDate` is `null` (because expiration depends on the semi-product's `ExpirationMonths`).

**Acceptance criteria:**
- Arrange: source `ManufactureOrder` with `SemiProduct = null` and at least one product.
- Assert: response has no error and exposes the persisted `Id` / `OrderNumber`.
- Assert on the captured `ManufactureOrder`:
  - `SemiProduct` is `null`.
  - `Products.Count` equals source `Products.Count`.
  - Each duplicated product has `ActualQuantity == source.PlannedQuantity` and `ExpirationDate == null`.
  - Each duplicated product has `LotNumber == ManufactureOrderExtensions.GetDefaultLot(fixedDateTime)`.

### FR-5: No regressions in existing tests
The change adds tests only; no production code is modified.

**Acceptance criteria:**
- `dotnet build` succeeds.
- `dotnet test` for the Manufacture test project succeeds — all new tests pass, all previously passing tests continue to pass.
- `dotnet format` introduces no diffs against the committed test file.

## Non-Functional Requirements

### NFR-1: Performance
Unit tests are in-process with mocked dependencies. Each test must complete in under 100 ms; the new test class must complete in under 1 second total locally.

### NFR-2: Security
No production code, configuration, or secrets are touched. No real database, file, or network access — the repository, user service, and time provider are fully mocked / faked.

### NFR-3: Maintainability
- Tests follow the AAA pattern and are named by behavior (e.g. `Handle_ReturnsOrderNotFound_WhenSourceOrderDoesNotExist`).
- Time, user, and order-number values are defined as `private const` / `private static readonly` fields at the top of the test class so the intent of each test is obvious.
- A small private helper (`BuildSourceOrder(...)`) is permitted to construct the source `ManufactureOrder` used by FR-3 / FR-4, provided it stays under ~30 lines and exposes the variables under test as parameters.

### NFR-4: Determinism
- Use `Microsoft.Extensions.Time.Testing.FakeTimeProvider` (already used in the codebase per the brief) — never `DateTime.UtcNow` or `Date.Now`-style calls.
- The lot number and expiration date asserted in tests are computed from the same fixed timestamp the handler sees, by calling the public static helpers on `ManufactureOrderExtensions` — do not hardcode formatted strings.

## Data Model
No new entities. Existing types exercised by the tests:
- `ManufactureOrder` (entity persisted via `IManufactureOrderRepository`).
- `ManufactureOrderSemiProduct` (optional child of `ManufactureOrder`).
- `ManufactureOrderProduct` (collection child of `ManufactureOrder`).
- `ManufactureOrderState` enum — duplicate is created in `Draft`.
- `ErrorCodes.OrderNotFound` — returned when source lookup fails.
- `DuplicateManufactureOrderRequest` / `DuplicateManufactureOrderResponse` — handler I/O.

## API / Interface Design
No public API changes. The tests target an existing MediatR handler:

```
DuplicateManufactureOrderHandler.Handle(DuplicateManufactureOrderRequest, CancellationToken)
    -> Task<DuplicateManufactureOrderResponse>
```

Mocked collaborators:
- `IManufactureOrderRepository` — `GetOrderByIdAsync`, `GenerateOrderNumberAsync`, `AddOrderAsync`.
- `ICurrentUserService` — `GetCurrentUser()` returning an object whose `GetDisplayName()` is exercised.
- `TimeProvider` — supplied as `FakeTimeProvider` at a fixed UTC instant.

## Dependencies
- xUnit (test runner).
- FluentAssertions.
- Mocking library already used in `backend/test/Anela.Heblo.Tests/Features/Manufacture/...` (Moq or NSubstitute — match what neighboring Manufacture tests use).
- `Microsoft.Extensions.Time.Testing` for `FakeTimeProvider` (verify the package is already referenced by the test project; if not, add the reference).
- The handler under test and `ManufactureOrderExtensions` — both already present in the codebase.

## Out of Scope
- Modifying handler production code — including the `if (sourceOrder.SemiProduct != null)` guard, the `ActualQuantity = PlannedQuantity` reset, the use of `DateTime.UtcNow` inside the handler for `CreatedDate` / `StateChangedAt`, or any logging additions. Existing behavior is locked in by the new tests; behavior changes are a separate task.
- Integration tests against a real `IManufactureOrderRepository` (database).
- Controller-level tests for `ManufactureOrderController.DuplicateManufactureOrder`.
- Coverage for unrelated Manufacture handlers (e.g. create, update, state transitions).
- Frontend or E2E coverage of the duplicate flow.

## Open Questions
None.

## Status: COMPLETE