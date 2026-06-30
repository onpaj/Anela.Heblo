# Architecture Review: Unit Tests for FlexiStockTakingDomainService

## Skip Design: true

## Architectural Fit Assessment

This task adds a new test file to an existing, well-established test project. No production code changes. The feature is purely additive and follows the established Adapter-layer unit-test pattern used throughout `Anela.Heblo.Adapters.Flexi.Tests`.

The SUT (`FlexiStockTakingDomainService`) is a straightforward orchestration class with five injectable dependencies and a single public method. All dependencies are interface-typed, which makes them trivially mockable with Moq. The test project already declares references to Moq, FluentAssertions, and xUnit — no new NuGet packages are required.

Key integration points:
- `IStockTakingClient` (SDK: `Rem.FlexiBeeSDK.Client.Clients.Products.StockTaking`) — `CreateHeaderAsync`, `GetHeaderAsync`, `AddMissingLotsAsync`, `SubmitAsync`
- `IStockTakingItemsClient` (same SDK namespace) — `AddStockTakingsAsync`, `GetStockTakingsAsync`
- `IStockTakingRepository` (domain interface) — `AddAsync`, `SaveChangesAsync`
- `ICurrentUserService` (domain interface) — `GetCurrentUser()` returns `CurrentUser` record
- `TimeProvider` (.NET 8 abstraction) — use `TimeProvider.System` or a `FakeTimeProvider` stub

`SoftStockTaking` is a **computed property** on `ErpStockTakingRequest` — it returns `true` only when every item in `StockTakingItems` has `SoftStockTaking == true`. To drive the two branches, populate items with `SoftStockTaking = true` (all items) or `SoftStockTaking = false` (at least one item).

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Adapters.Flexi.Tests (test project)
└── Stock/
    ├── FlexiStockClientTests.cs          (exists — neighbour)
    └── FlexiStockTakingDomainServiceTests.cs  (NEW — one file, one class)

SUT:
FlexiStockTakingDomainService
  ├── Mock<IStockTakingClient>
  ├── Mock<IStockTakingItemsClient>
  ├── Mock<IStockTakingRepository>
  ├── Mock<ICurrentUserService>
  └── TimeProvider.System  (real, no time assertions needed)
```

All five tests share a single constructor that wires up the mocks and the SUT. Only the mocks relevant to each scenario need `.Setup(...)` calls — all others default to `MockBehavior.Loose` and do nothing.

### Key Design Decisions

#### Decision 1: MockBehavior for clients
**Options considered:**
- `MockBehavior.Loose` (default) — unsetup calls return `default`/`null`
- `MockBehavior.Strict` — any unexpected call throws

**Chosen approach:** `MockBehavior.Loose` for all mocks.

**Rationale:** The SUT calls several methods in sequence; strict mocks would require exhaustive setup for every call in every test, making the tests brittle and hard to read. The exception-path test already verifies no repository save occurs, which is the critical strict-boundary assertion. Use `Verifiable()` / `.Verify(Times.Never)` selectively where "must not be called" is part of the contract.

#### Decision 2: Returning mock data from IStockTakingItemsClient
**Options considered:**
- Return real SDK model instances with properties set
- Return empty lists

**Chosen approach:** Return minimal but valid `StockTakingItemResult` instances with `AmountFound` and `AmountErp` set to known values so that the `AmountNew` / `AmountOld` assertions on the returned `StockTakingRecord` are deterministic.

**Rationale:** `AmountNew` is `itemsAfter.Sum(s => s.AmountFound)` and `AmountOld` is `itemsBefore.Sum(s => s.AmountErp)`. Tests that only assert on mock-call counts can return empty lists, but any test asserting the record values needs non-zero returns to distinguish zero-because-empty from zero-because-wrong.

#### Decision 3: Repository verification in the exception path
**Options considered:**
- Verify `AddAsync` not called
- Verify `SaveChangesAsync` not called
- Assert both

**Chosen approach:** Assert both `AddAsync` and `SaveChangesAsync` are never called when an exception is thrown.

**Rationale:** The catch block constructs and returns a `StockTakingRecord` without touching the repository. Asserting only one method leaves a gap. Both assertions together prove the entire persistence side-effect is suppressed, which is the stated risk in the brief.

## Implementation Guidance

### Directory / Module Structure

Create exactly one new file:

```
backend/test/Anela.Heblo.Adapters.Flexi.Tests/Stock/FlexiStockTakingDomainServiceTests.cs
```

Namespace: `Anela.Heblo.Adapters.Flexi.Tests.Stock`

No changes to `.csproj` required — test discovery is automatic.

### Interfaces and Contracts

**SDK types to import:**
```csharp
using Rem.FlexiBeeSDK.Client.Clients.Products.StockTaking;  // IStockTakingClient, IStockTakingItemsClient
using Rem.FlexiBeeSDK.Model.Products.StockTaking;           // StockTakingHeader, StockTakingItemResult
```

**Domain types:**
```csharp
using Anela.Heblo.Adapters.Flexi.Stock;                     // FlexiStockTakingDomainService
using Anela.Heblo.Domain.Features.Catalog.Stock;            // ErpStockTakingRequest, ErpStockTakingLot, StockTakingRecord
using Anela.Heblo.Domain.Features.Users;                    // ICurrentUserService, CurrentUser
```

**Constructor wire-up (shared across all tests):**
```csharp
_sut = new FlexiStockTakingDomainService(
    _mockRepository.Object,
    _mockStockTakingClient.Object,
    _mockStockTakingItemsClient.Object,
    _mockCurrentUser.Object,
    TimeProvider.System);
```

`ICurrentUserService.GetCurrentUser()` must be stubbed to return a valid `CurrentUser` record in all tests, e.g.:
```csharp
_mockCurrentUser.Setup(x => x.GetCurrentUser())
    .Returns(new CurrentUser("user-1", "Test User", "test@example.com", true));
```

### Data Flow

#### Happy path — real stock taking (SoftStockTaking=false, DryRun=false, RemoveMissingLots=false)

```
test calls SubmitStockTakingAsync(order)
  → CreateHeaderAsync(headerRequest) → returns StockTakingHeader { Id = 42 }
  → AddStockTakingsAsync(42, 5, newItems)
  → GetStockTakingsAsync(42) [itemsBefore]   → returns list with AmountErp
  → SubmitAsync(42, 60)
  → GetHeaderAsync(42)                        → returns updated header
  → GetStockTakingsAsync(42) [itemsAfter]    → returns list with AmountFound
  → repository.AddAsync(result)
  → repository.SaveChangesAsync()
  → returns StockTakingRecord { AmountNew != 0, AmountOld != 0, Error == null }
```

#### SoftStockTaking=true path

```
test calls SubmitStockTakingAsync(order where all items.SoftStockTaking=true)
  → (no ERP calls at all)
  → repository.AddAsync(result)
  → repository.SaveChangesAsync()
  → returns StockTakingRecord { AmountNew == AmountOld == sum(order.StockTakingItems.Amount) }
```

#### DryRun=true path

Same as happy path except `SubmitAsync` must NOT be called. Assert with `Times.Never`.

#### RemoveMissingLots=true path

An extra `GetStockTakingsAsync` call occurs between `AddStockTakingsAsync` and the `itemsBefore` `GetStockTakingsAsync`, followed by `AddMissingLotsAsync`. The SDK call sequence is:
1. `CreateHeaderAsync`
2. `AddStockTakingsAsync`
3. `GetStockTakingsAsync` (for `RemoveMissingLots` — returns items with `ProductId`)
4. `AddMissingLotsAsync(headerId, productIds)`
5. `GetStockTakingsAsync` (itemsBefore)
6. `SubmitAsync` (if not DryRun)
7. `GetHeaderAsync`
8. `GetStockTakingsAsync` (itemsAfter)

Moq's `SetupSequence` is not needed — setting up `GetStockTakingsAsync` to always return the same list is fine for this test.

#### Exception path

```
CreateHeaderAsync throws new Exception("ERP unavailable")
  → catch block fires
  → returns StockTakingRecord { Error = "ERP unavailable", AmountOld = 0 (sum of items = 0 for empty list or known value) }
  → repository.AddAsync never called
  → repository.SaveChangesAsync never called
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `GetStockTakingsAsync` is called twice in `RemoveMissingLots=true` path (once for lots, once for itemsBefore); a single loose setup returns the same list both times, potentially masking a bug if the second call was accidentally removed | Low | Accept — the test verifies `AddMissingLotsAsync` was called with correct `productIds`, which is the contract under test; the double-call semantics are an ERP concern, not a domain concern |
| `SoftStockTaking` is computed from items, not a direct bool field — a test that passes `SoftStockTaking=false` as if it were a direct property will fail to compile | High | Always construct `ErpStockTakingRequest` with `StockTakingItems` containing at least one `ErpStockTakingLot` with `SoftStockTaking = false` to trigger the ERP path |
| SDK interfaces are not in the test project's namespace — forgetting the correct `using` directives causes compiler errors that look like mock-setup failures | Low | List explicit `using` directives in the test file header as documented above |
| `AmountOld` in the catch block is set to `order.StockTakingItems.Sum(s => s.Amount)`, NOT `AmountOld = 0` — if the test creates items with non-zero `Amount`, the assertion `AmountOld == expectedAmount` must match | Medium | Use a request with a single item of known `Amount` (e.g. `10m`) in the exception test to make `AmountOld` predictable; assert `result.AmountOld == 10.0` to confirm the catch-block code path ran |

## Specification Amendments

**Amendment 1 — FR-4 clarification (RemoveMissingLots call sequence)**
The spec says "AddMissingLotsAsync called after adding items". Based on the source, the sequence is: `AddStockTakingsAsync` → `GetStockTakingsAsync` (to obtain `ProductId` list) → `AddMissingLotsAsync`. The test must verify `AddMissingLotsAsync` is called with the `productIds` extracted from the intermediate `GetStockTakingsAsync` result, not just that it is called at all.

**Amendment 2 — Exception test assertion precision**
The spec says "returned record has Error field set (not null/empty)". Additionally assert `result.Code == order.ProductCode` and `result.Date != default` (both are set in the catch block) to confirm the catch-block record is fully constructed, not a default-initialized `StockTakingRecord`.

**Amendment 3 — SoftStockTaking test: AmountNew == AmountOld assertion**
The spec is correct. Additionally assert that `AmountNew == (double)order.StockTakingItems.Sum(s => s.Amount)` explicitly, not just that the two fields are equal to each other, to guard against a future bug where both are accidentally set to zero.

## Prerequisites

None. All dependencies exist:
- Test project `Anela.Heblo.Adapters.Flexi.Tests.csproj` is present with Moq, FluentAssertions, xUnit
- `InternalsVisibleTo("Anela.Heblo.Adapters.Flexi.Tests")` is declared in the production `.csproj`
- `Rem.FlexiBeeSDK.Client` v0.1.138 is already referenced in the production project and transitively available to the test project via `ProjectReference`
- No migrations, config changes, or infrastructure work required
