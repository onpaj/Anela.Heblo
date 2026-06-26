# Architecture Review: Unit Test Coverage for SubmitStockTakingHandler

## Skip Design: true

This is a backend-only test addition. No UI/UX components, screens, layouts, or visual decisions are involved.

## Architectural Fit Assessment

The feature is a textbook fit for the existing architecture:

- **Vertical Slice + handler/test mirroring.** The project's test layout mirrors the application layout (`src/.../Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingHandler.cs` ↔ `test/.../Features/Catalog/SubmitStockTakingHandlerTests.cs`). Adding one test file in `backend/test/Anela.Heblo.Tests/Features/Catalog/` slots in next to the existing 25+ handler test classes (`GetCatalogDetailHandlerTests`, `AcceptStockUpOperationHandlerTests`, etc.).
- **Identical test stack already validated.** `SubmitManufactureStockTakingHandlerTests.cs` already uses xUnit + FluentAssertions + Moq with the AAA convention and inline `// Arrange / // Act / // Assert` comments. The sibling `SubmitStockTakingHandler` has the same shape (three constructor deps: `ICatalogRepository`, `ILogger<>`, `IEshopStockDomainService`) and the same response pattern (`BaseResponse` subclass with `ErrorCode` + `Params`). Reusing the sibling fixture as a structural template is the obvious choice — no architectural daylight between the two.
- **No production-code coupling impact.** The work touches only the `Anela.Heblo.Tests` assembly. No DI registration changes, no new interfaces, no shared fixtures to extend.
- **Single integration point worth flagging:** `CatalogAggregate.SyncStockTaking(StockTakingRecord)` (verified at `CatalogAggregate.cs:296-321`) throws `ArgumentException` if `record.Code != ProductCode`. The happy-path test (FR-3) must keep the record's `Code` equal to the request's `ProductCode` or the test will fail for the wrong reason.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│  SubmitStockTakingHandlerTests  (new file, xUnit class)              │
│                                                                      │
│  ctor:                                                               │
│    _catalogRepositoryMock      : Mock<ICatalogRepository>            │
│    _eshopStockDomainServiceMock: Mock<IEshopStockDomainService>      │
│    _loggerMock                 : Mock<ILogger<SubmitStockTakingHandler>>│
│    _handler                    : SubmitStockTakingHandler (SUT)      │
│                                                                      │
│  Tests (one per path of Handle):                                     │
│    [Fact] Handle_DomainServiceReturnsError_*                         │
│    [Fact] Handle_DomainServiceSucceeds_ProductNotFound_*             │
│    [Fact] Handle_DomainServiceSucceeds_ProductFound_*                │
│    [Fact] Handle_DomainServiceThrows_*                               │
│                                                                      │
│  Helpers:                                                            │
│    CreateRequest(productCode, targetAmount?, soft?)                  │
│    CreateSuccessfulStockTakingRecord(productCode)                    │
│    CreateFailedStockTakingRecord(productCode, error)                 │
│    CreateCatalogAggregate(productCode)                               │
└──────────────────────────────────────────────────────────────────────┘
              │ exercises
              ▼
┌──────────────────────────────────────────────────────────────────────┐
│  SubmitStockTakingHandler.Handle   (unchanged production code)       │
│   ├─► IEshopStockDomainService.SubmitStockTakingAsync  (mock)        │
│   ├─► ICatalogRepository.GetByIdAsync                  (mock)        │
│   └─► CatalogAggregate.SyncStockTaking                 (real)        │
└──────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Real `CatalogAggregate` vs. mocked aggregate for the happy path
**Options considered:**
- (a) Mock `CatalogAggregate` and verify `SyncStockTaking` was invoked.
- (b) Use a real `CatalogAggregate` and assert the resulting side effects (`StockTakingHistory`, `Stock.Eshop`).

**Chosen approach:** (b) — instantiate `CatalogAggregate` via its public init properties and let the real `SyncStockTaking` run.

**Rationale:** `CatalogAggregate` is a domain entity, not an interface, so Moq cannot mock its method directly (the method is non-virtual). More importantly, `SubmitManufactureStockTakingHandlerTests` already uses real aggregates (see `CreateMaterialWithLots`, `CreateMaterialWithoutLots` in the sibling file). Asserting on `aggregate.StockTakingHistory.Contains(record)` and `aggregate.Stock.Eshop == (decimal)record.AmountNew` gives the same regression value with stronger fidelity to production behavior. The aggregate's `Code != ProductCode` guard is a real risk the test will then catch for free.

#### Decision 2: Single-file fixture vs. shared base
**Options considered:**
- (a) Inherit from a shared abstract test base.
- (b) Self-contained class with private helpers (mirrors `SubmitManufactureStockTakingHandlerTests`).

**Chosen approach:** (b).

**Rationale:** There is no `HandlerTestBase` precedent in `backend/test/Anela.Heblo.Tests/Features/Catalog/` — every handler test class stands alone. Introducing a base for one new file would violate YAGNI and surgical-changes guidance (CLAUDE.md). Match what is already there.

#### Decision 3: How to document the silent-correctness hole (FR-2)
**Options considered:**
- (a) Plain test that observes the current behavior with no callout.
- (b) Test annotated with an inline comment naming the documented hole and pointing to the brief/spec, plus an `// IMPORTANT:` line explaining what regression this would catch.

**Chosen approach:** (b).

**Rationale:** Spec FR-2 explicitly asks the test to "serve as a regression guard for the documented silent-correctness hole." A future developer changing the handler to surface an error here must see — directly in the test source — that the existing assertion locks in **current** (intentionally accepted) behavior, not desired behavior. The Out-of-Scope section of the spec confirms this: fixing the hole is a separate ticket.

#### Decision 4: Verifying `SyncStockTaking` was NOT called in FR-2
**Options considered:**
- (a) Implicit — `GetByIdAsync` returns `null` so the call cannot happen.
- (b) Pass a real `CatalogAggregate` spy and assert `StockTakingHistory.Count == 0`.

**Chosen approach:** (a), with an inline comment as the spec requests.

**Rationale:** `_catalogRepositoryMock.Setup(... ).ReturnsAsync((CatalogAggregate?)null)` makes the missed branch structurally unreachable. Adding a spy aggregate to "verify the negative" is dead weight that obscures the test's intent. The comment makes the assertion explicit without code.

## Implementation Guidance

### Directory / Module Structure

Create exactly one new file:

```
backend/test/Anela.Heblo.Tests/
└── Features/
    └── Catalog/
        └── SubmitStockTakingHandlerTests.cs   ← NEW
```

No new folders. No changes to project files (`Anela.Heblo.Tests.csproj` already references both `Anela.Heblo.Application` and `Anela.Heblo.Domain`, transitively pulling in xUnit/FluentAssertions/Moq from the sibling test).

### Interfaces and Contracts

The test interacts only with **existing** types. The contracts to honor:

| Type | Source | Use in tests |
|---|---|---|
| `SubmitStockTakingHandler` | `Application/Features/Catalog/UseCases/SubmitStockTaking/` | SUT, constructed in ctor |
| `SubmitStockTakingRequest` | same folder | `class` with `ProductCode`, `TargetAmount`, `SoftStockTaking` — instantiate via object initializer |
| `SubmitStockTakingResponse` | same folder | `class : BaseResponse`; parameterless ctor → `Success == true`; `(ErrorCodes, Dictionary<string,string>?)` ctor → `Success == false` |
| `ICatalogRepository` | `Domain/Features/Catalog/` | mocked; only `GetByIdAsync(string, CancellationToken)` is called |
| `IEshopStockDomainService` | `Domain/Features/Catalog/Stock/` | mocked; only `SubmitStockTakingAsync(EshopStockTakingRequest)` is called |
| `EshopStockTakingRequest` | `Domain/Features/Catalog/Stock/` | use `It.IsAny<EshopStockTakingRequest>()` or `It.Is<>` matcher to verify mapping |
| `StockTakingRecord` | `Domain/Features/Catalog/Stock/` | instantiated as a real record/POCO; `Code` MUST equal request `ProductCode` in FR-3 (`SyncStockTaking` validates this — `CatalogAggregate.cs:301`) |
| `CatalogAggregate` | `Domain/Features/Catalog/` | real instance in FR-3 via public init properties (mirror the sibling test's helper) — must have `Stock = new StockData()` to avoid NRE in `SyncStockTaking` |
| `ErrorCodes` | `Application.Shared` | enum constants `StockTakingFailed`, `InternalServerError` |

### Data Flow

**FR-1 — Domain service returns error:**
```
Test → Handler.Handle(request)
       → _eshopStockDomainService.SubmitStockTakingAsync(...)  [mock returns record with Error="X"]
       → branch: !string.IsNullOrEmpty(record.Error) ⇒ return response(ErrorCodes.StockTakingFailed, params)
Test asserts: response.ErrorCode == StockTakingFailed
              response.Params["ProductCode"] == request.ProductCode
              response.Params["Error"] == "X"
              _catalogRepository.GetByIdAsync was never invoked
```

**FR-2 — Domain succeeds, product not found (silent-correctness hole):**
```
Test → Handler.Handle(request)
       → _eshopStockDomainService.SubmitStockTakingAsync(...)  [mock returns record, Error=null]
       → _catalogRepository.GetByIdAsync(...)                  [mock returns null]
       → branch: product == null ⇒ skip SyncStockTaking
       → return response { Id, Type, Code, AmountNew, AmountOld, Date, User, Error } (Success=true)
Test asserts: response.Success == true
              response field-by-field equals stockTakingRecord
              GetByIdAsync invoked once
              // No SyncStockTaking call possible — product is null. Regression guard.
```

**FR-3 — Happy path:**
```
Test → Handler.Handle(request)
       → mock domain service returns record with Code == request.ProductCode
       → mock repo returns real CatalogAggregate with matching ProductCode
       → product.SyncStockTaking(record)  [REAL — adds to StockTakingHistory, sets Stock.Eshop]
       → return response populated from record
Test asserts: response.Success == true
              response field-by-field equals stockTakingRecord
              aggregate.StockTakingHistory contains the record
              GetByIdAsync invoked once with (ProductCode, ct)
```

**FR-4 — Exception path:**
```
Test → Handler.Handle(request)
       → _eshopStockDomainService.SubmitStockTakingAsync(...) throws
       → catch ⇒ logger.LogError, return response(ErrorCodes.InternalServerError, {"ProductCode": ...})
Test asserts: no exception bubbled
              response.ErrorCode == InternalServerError
              response.Params["ProductCode"] == request.ProductCode
              GetByIdAsync never invoked
```

### Concrete File Skeleton

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.SubmitStockTaking;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class SubmitStockTakingHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock = new();
    private readonly Mock<IEshopStockDomainService> _eshopStockDomainServiceMock = new();
    private readonly Mock<ILogger<SubmitStockTakingHandler>> _loggerMock = new();
    private readonly SubmitStockTakingHandler _handler;

    public SubmitStockTakingHandlerTests()
    {
        _handler = new SubmitStockTakingHandler(
            _catalogRepositoryMock.Object,
            _loggerMock.Object,
            _eshopStockDomainServiceMock.Object);
    }

    // 4 [Fact] tests, one per FR. Helpers below.

    private static SubmitStockTakingRequest CreateRequest(string productCode, decimal targetAmount = 100m, bool soft = false) => new()
    { ProductCode = productCode, TargetAmount = targetAmount, SoftStockTaking = soft };

    private static StockTakingRecord CreateSuccessRecord(string productCode) => new()
    { Id = 1001, Code = productCode, Type = StockTakingType.Eshop, AmountOld = 10, AmountNew = 50,
      Date = new DateTime(2026, 6, 8), User = "tester", Error = null };

    private static CatalogAggregate CreateAggregate(string productCode) => new()
    { ProductCode = productCode, ProductName = $"Product {productCode}",
      Type = ProductType.Goods, Stock = new StockData() };
}
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `CatalogAggregate.SyncStockTaking` throws if `record.Code != ProductCode` — happy-path test silently fails for the wrong reason | Medium | Centralize `productCode` in a local variable shared by request, record, and aggregate within FR-3. |
| `StockData` is a non-trivial value object; constructing a `CatalogAggregate` without it causes NRE inside `SyncStockTaking` (`Stock.Eshop = ...`) | Medium | Always initialize `Stock = new StockData()` in the helper (same as sibling test). |
| `SubmitStockTakingResponse` is a class with mutable setters — comparing field-by-field can drift if new fields are added later | Low | Assert each spec field explicitly (`Id`, `Type`, `Code`, `AmountNew`, `AmountOld`, `Date`, `User`, `Error`) rather than `.BeEquivalentTo`. Spec lists exactly these fields, so additions become an intentional spec change. |
| Future maintainer fixes the silent-correctness hole (FR-2) and breaks the test, not realizing the test was a deliberate regression guard | Medium | Inline comment at the top of FR-2 test naming the hole, citing the spec section, and stating "this test locks in current behavior; changing it requires a deliberate decision to surface an error." |
| Mock setup omitted for `GetByIdAsync` in FR-1/FR-4 could return a non-null default and accidentally pass | Low | Strict path: in FR-1/FR-4 do not set up `GetByIdAsync` at all and assert `Times.Never` — defaults are `null` for reference types in Moq's loose mode. |
| `DateTime.UtcNow` or `DateTime.Now` in helpers produces non-deterministic tests | Low | Use a fixed `DateTime` literal in helpers (mirrors immutability/determinism preference). |

## Specification Amendments

The spec is internally consistent and grounded in the actual code. Two small additions for the implementer to avoid foreseeable failures:

1. **FR-3 acceptance criteria addendum:** When constructing the real `CatalogAggregate`, set `Stock = new StockData()`. Without it `SyncStockTaking` will throw at `Stock.Eshop = newStockLevel` (`CatalogAggregate.cs:316`).
2. **FR-3 acceptance criteria addendum:** The mocked `StockTakingRecord` MUST have `Code == request.ProductCode` and `Type ∈ { Eshop, Erp }`. `SyncStockTaking` throws `ArgumentException` on code mismatch (`CatalogAggregate.cs:301`) and `ArgumentOutOfRangeException` on unknown types (`CatalogAggregate.cs:319`).
3. **FR-2 wording clarification:** The acceptance criterion currently says verification via `Times.Never` is "implicit" because the mock returns `null`. This is correct but easy to misread as "no assertion needed." The implementer should add an explicit inline code comment in the test body (e.g. `// SyncStockTaking is unreachable: product == null. This documents the silent-correctness hole described in brief.md path 2.`) rather than only relying on the spec's "call it out as a comment" instruction.

These are clarifications, not changes — no behavior or scope shift.

## Prerequisites

None. All required pieces already exist:

- xUnit, FluentAssertions, Moq are already referenced in `Anela.Heblo.Tests` (verified via the sibling `SubmitManufactureStockTakingHandlerTests`).
- All production types (`SubmitStockTakingHandler`, `SubmitStockTakingRequest/Response`, `ICatalogRepository`, `IEshopStockDomainService`, `CatalogAggregate`, `StockTakingRecord`, `ErrorCodes`) exist and have the shapes the spec assumes.
- No migrations, no config, no DI registration changes, no new NuGet packages.

Implementation can start immediately. Validation before completion: `dotnet build` + `dotnet test --filter FullyQualifiedName~SubmitStockTakingHandlerTests` + `dotnet format`.