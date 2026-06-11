I have enough grounding to write the review. The task tracking reminder is irrelevant for a single-output architecture review.

```markdown
# Architecture Review: Unit Tests for DuplicateManufactureOrderHandler

## Skip Design: true

## Architectural Fit Assessment

This is a pure test-coverage addition for an existing MediatR handler at `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs:8`. No production code, no public contracts, no UI. The handler already follows the established Vertical Slice + MediatR pattern: it depends on three abstractions (`IManufactureOrderRepository`, `ICurrentUserService`, `TimeProvider`), all of which are trivially substitutable in a unit test. Integration with the existing test project is mechanical — the same handler-test recipe used across the Manufacture module applies here.

The only architectural friction is a small mismatch between the spec's proposed folder layout / dependencies and what the test project actually does:

- Existing convention in `backend/test/Anela.Heblo.Tests/Features/Manufacture/` is **flat** — one `*HandlerTests.cs` per handler at the module root, not nested under `UseCases/<Slice>/`. See `GetManufactureOrderHandlerTests.cs`, `CreateManufactureOrderHandlerSinglePhaseTests.cs`, etc. The spec's proposed path `Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandlerTests.cs` would be the only nested file in the module.
- `Microsoft.Extensions.Time.Testing` is **not** referenced by `Anela.Heblo.Tests.csproj` today. The only `FakeTimeProvider` in the test tree is a hand-rolled inner class inside `GetPackingDashboardHandlerTests.cs`. The neighbouring `CreateManufactureOrderHandlerSinglePhaseTests` mocks `TimeProvider` with `Mock<TimeProvider>` and stubs `GetUtcNow()` — no package added.
- Test classes in this module use `public class`, **not** `public sealed`. Mocking library is Moq (uniform across every Manufacture handler test).

These divergences are minor but should be aligned with existing conventions rather than introducing a one-off pattern in this file. Amendments below.

## Proposed Architecture

### Component Overview

```
DuplicateManufactureOrderHandlerTests (xUnit, Moq, FluentAssertions)
        │
        ├── SUT: DuplicateManufactureOrderHandler
        │
        ├── Mock<IManufactureOrderRepository>
        │       ├── GetOrderByIdAsync(sourceId, ct)   → ManufactureOrder | null
        │       ├── GenerateOrderNumberAsync(ct)      → "MO-2026-0042"
        │       └── AddOrderAsync(order, ct)          → captures arg, returns with Id
        │
        ├── Mock<ICurrentUserService>
        │       └── GetCurrentUser()                  → CurrentUser(..., name, ..., true)
        │
        └── Mock<TimeProvider>
                └── GetUtcNow()                       → fixed DateTimeOffset
```

Assertions on the captured `ManufactureOrder` are the central mechanism — the handler is a transform, so the test surface is "what got passed into `AddOrderAsync`". The expected lot / expiration values are computed in the test by calling the same public statics the handler calls (`ManufactureOrderExtensions.GetDefaultLot`, `GetDefaultExpiration`), against the same fixed instant — no hardcoded format strings.

### Key Design Decisions

#### Decision 1: TimeProvider substitute — `Mock<TimeProvider>` over `Microsoft.Extensions.Time.Testing.FakeTimeProvider`
**Options considered:**
- A. Add `Microsoft.Extensions.Time.Testing` to the test csproj, use `FakeTimeProvider` (what the spec proposes).
- B. Use `Mock<TimeProvider>` with `Setup(x => x.GetUtcNow()).Returns(fixed)` — matches `CreateManufactureOrderHandlerSinglePhaseTests.cs:18` exactly.
- C. Promote the hand-rolled `FakeTimeProvider` in `GetPackingDashboardHandlerTests.cs` to a shared test helper.

**Chosen approach:** B — `Mock<TimeProvider>`.

**Rationale:** The handler only calls `_timeProvider.GetUtcNow()`. A single Moq stub covers it. Option A adds a package the project doesn't currently consume, for no behavioral benefit on this surface. Option C is a separate refactor with no value in scope here. Match the neighbour-handler pattern; do not introduce a new dependency for one test class.

#### Decision 2: File location — flat under `Features/Manufacture/`
**Options considered:**
- A. Spec-proposed nested path `Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandlerTests.cs`.
- B. Flat `Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs`.

**Chosen approach:** B.

**Rationale:** Every other handler test in the Manufacture module is flat. Introducing a single nested path for this one file fragments the convention with no offsetting benefit. The brief in the spec already permits this ("align if the module already uses a different folder"); the existing folder structure decides it.

#### Decision 3: Capture-and-assert via Moq `Callback`
**Options considered:**
- A. `It.Is<ManufactureOrder>(...)` matcher with all assertions inline.
- B. `Setup(...).Callback<ManufactureOrder, CancellationToken>(o => captured = o).ReturnsAsync(...)`, then assert with FluentAssertions on the captured instance.

**Chosen approach:** B.

**Rationale:** The duplicate has ~12 fields plus a child collection; inline matchers explode in size and produce poor failure messages. Capturing once and asserting with FluentAssertions yields one diagnostic per failing field. This is the prevailing style in the module (e.g. `ConfirmSemiProductManufactureHandlerTests.cs`).

#### Decision 4: Lot / expiration assertions reference the static helpers, not literal strings
The expected `LotNumber` and `ExpirationDate` are computed in-test as `ManufactureOrderExtensions.GetDefaultLot(fixedDateTime)` and `ManufactureOrderExtensions.GetDefaultExpiration(fixedDateTime, expirationMonths)`. This is already in the spec (NFR-4) and is correct — locks in the handler's *use of* the helper, not the helper's formula. If the helper changes, this test still passes (intentional); a separate `ManufactureOrderExtensionsTests` owns the formula.

## Implementation Guidance

### Directory / Module Structure

```
backend/test/Anela.Heblo.Tests/Features/Manufacture/
└── DuplicateManufactureOrderHandlerTests.cs       ← new (flat, matches sibling files)
```

No other files. No new project, no shared helper extraction. `Anela.Heblo.Tests.csproj` is not modified.

### Interfaces and Contracts

No new types. The test class consumes:

| Type | Source | Role |
|---|---|---|
| `DuplicateManufactureOrderHandler` | `Anela.Heblo.Application.Features.Manufacture.UseCases.DuplicateManufactureOrder` | SUT |
| `DuplicateManufactureOrderRequest` / `DuplicateManufactureOrderResponse` | same namespace | I/O |
| `IManufactureOrderRepository` | `Anela.Heblo.Domain.Features.Manufacture` | mocked |
| `ICurrentUserService` / `CurrentUser` | `Anela.Heblo.Domain.Features.Users` | mocked / constructed |
| `TimeProvider` | `System` | mocked |
| `ManufactureOrder`, `ManufactureOrderSemiProduct`, `ManufactureOrderProduct`, `ManufactureOrderState` | `Anela.Heblo.Domain.Features.Manufacture` | constructed as fixtures |
| `ManufactureOrderExtensions` | same | called for expected-value computation only |
| `ErrorCodes` | `Anela.Heblo.Application.Shared` | assertion |

Constructor signature for `CurrentUser` (verified in `CreateManufactureOrderHandlerSinglePhaseTests.cs:61`): `new CurrentUser("testuser", "Test User", "test@example.com", true)`.

### Data Flow

**FR-2 (not found):**
```
Request{SourceOrderId=42}
  → repo.GetOrderByIdAsync(42, ct)            ← returns null
  → return Response(ErrorCodes.OrderNotFound)
  → GenerateOrderNumberAsync, AddOrderAsync   ← Verify(Times.Never)
```

**FR-3 (with semi-product) / FR-4 (without semi-product):**
```
Request{SourceOrderId=42}
  → repo.GetOrderByIdAsync(42, ct)            ← returns built fixture
  → currentUserService.GetCurrentUser()       ← returns CurrentUser
  → repo.GenerateOrderNumberAsync(ct)         ← returns "MO-2026-0042"
  → timeProvider.GetUtcNow()                  ← returns 2026-06-08T10:00:00Z
  → repo.AddOrderAsync(captured, ct)          ← returns captured with Id = 1234
  → return Response{ Id=1234, OrderNumber="MO-2026-0042" }

Assert on `captured`:
  OrderNumber, State=Draft, CreatedByUser, StateChangedByUser, ResponsiblePerson, PlannedDate
  SemiProduct (null in FR-4; populated in FR-3)
  Products[*] (ActualQuantity == source.PlannedQuantity, ExpirationDate from helper or null)
```

### Test class skeleton (illustrative — do not copy verbatim, match local style)

```csharp
public class DuplicateManufactureOrderHandlerTests
{
    private const int SourceOrderId = 42;
    private const int PersistedOrderId = 1234;
    private const string GeneratedOrderNumber = "MO-2026-0042";
    private const string DisplayName = "Test User";
    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<IManufactureOrderRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly DuplicateManufactureOrderHandler _handler;

    public DuplicateManufactureOrderHandlerTests()
    {
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(FixedNow);
        _currentUserServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("u1", DisplayName, "u1@example.com", true));
        _handler = new DuplicateManufactureOrderHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _timeProviderMock.Object);
    }

    // [Fact] Handle_ReturnsOrderNotFound_WhenSourceOrderDoesNotExist
    // [Fact] Handle_DuplicatesAllFields_WhenSourceHasSemiProductAndProducts
    // [Fact] Handle_OmitsSemiProductAndLeavesProductExpirationNull_WhenSourceHasNoSemiProduct
}
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Spec's proposed path / dependency / `sealed` modifier diverge from module convention, fragmenting the test project. | LOW | Apply amendments below — flat path, `Mock<TimeProvider>`, `public class`, Moq. |
| Test asserts a hardcoded lot string and breaks when `GetDefaultLot`'s format changes. | LOW | NFR-4 already mandates calling the static helper for expected values; honor it. |
| Test depends on `CurrentUser` constructor order; `CurrentUser` is in the Domain (not OpenAPI), so the project-wide "DTOs are classes, not records" rule does not constrain it — but check whether it's currently a record before assuming positional ctor. | LOW | Use the same construction call already in `CreateManufactureOrderHandlerSinglePhaseTests.cs:61` — if that compiles today, the new test compiles. |
| `Mock<TimeProvider>` over a non-virtual base could fail if `GetUtcNow` is sealed in some BCL versions. | LOW | The neighbouring `CreateManufactureOrderHandlerSinglePhaseTests` does this successfully on net8.0 — same target framework. |
| Capture-via-Callback masks a missed call site (e.g. handler stops calling `AddOrderAsync`). | LOW | The happy-path tests also assert on `response.Id` / `response.OrderNumber`, which only come back if `AddOrderAsync` ran. |

## Specification Amendments

1. **FR-1 path** — Change the target path from `backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandlerTests.cs` to `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs`. Rationale: every existing `*HandlerTests.cs` in the module is flat at this folder.
2. **FR-1 class modifier** — Drop `sealed`; use `public class DuplicateManufactureOrderHandlerTests`. Rationale: matches the rest of the module.
3. **NFR-4 / Dependencies** — Replace `Microsoft.Extensions.Time.Testing.FakeTimeProvider` with `Mock<TimeProvider>` and a `Setup(x => x.GetUtcNow()).Returns(fixed)`. Do **not** add `Microsoft.Extensions.Time.Testing` to `Anela.Heblo.Tests.csproj`. Rationale: `CreateManufactureOrderHandlerSinglePhaseTests.cs` already establishes this pattern with the exact same handler dependency; introducing a new package for one file is unjustified. (The spec itself anticipates this — "verify the package is already referenced … if not, add the reference" — and the answer is: it isn't, and we shouldn't.)
4. **Mocking library** — Confirm Moq (not NSubstitute). Every handler test in this module uses Moq; mixing libraries in one test class is undesirable.
5. **FR-3 PlannedDate assertion** — Source spec states `DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime)`. With the fixed `DateTimeOffset` chosen above, prefer `DateOnly.FromDateTime(FixedNow.UtcDateTime)` in the assertion to avoid local-time drift surprises.

## Prerequisites

None. The handler, all collaborators, `ManufactureOrderExtensions`, Moq, FluentAssertions, and xUnit are already in place. The test project compiles against `net8.0` with `TimeProvider` available from BCL. No migrations, no config, no infrastructure.
```