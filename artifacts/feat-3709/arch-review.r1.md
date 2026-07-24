# Architecture Review: Unit Test Coverage for GetMaterialsForPurchaseHandler

## Skip Design: true

## Architectural Fit Assessment
This is a test-only addition to an already-implemented, stable vertical slice (`Catalog/UseCases/GetMaterialForPurchase`). No production code, contracts, DI wiring, or API surface changes. It aligns cleanly with existing conventions:

- The target handler (`GetMaterialsForPurchaseHandler.cs`) is a standard MediatR `IRequestHandler` with a single constructor-injected dependency (`ICatalogRepository`), the same shape as `GetProductUsageHandler` and dozens of other handlers already covered under `backend/test/Anela.Heblo.Tests/Features/Catalog/`.
- `docs/architecture/testing-strategy.md` explicitly requires unit tests for "All business logic, validation, error scenarios" in MediatR handlers, mocking dependencies with Moq and asserting with FluentAssertions — exactly the pattern the spec proposes.
- Confirmed via source inspection: no test file currently references `GetMaterialsForPurchaseHandler` (`GetMaterialsForPurchaseHandlerTests.cs` does not exist under `backend/test`), and the handler itself (read in full, 51 lines) has no branches or dependencies the spec's FR list fails to account for.
- `CatalogAggregate.PurchaseHistory` (in `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs`) is a plain `IReadOnlyList<CatalogPurchaseRecord>` property with a public setter that also triggers `UpdatePurchaseHistorySummary()` as a side effect. This confirms the spec's "Data Model" note: fixtures can set `PurchaseHistory` directly via object initializer — no special mutation API is required, and the summary recompute is inert for this handler's purposes (it doesn't read `PurchaseHistorySummary`).
- No test-coverage-gap task of this shape (mocked-repository handler unit tests) requires any new abstraction, module boundary change, or infrastructure — it is additive test code in the existing test project, following the `GetProductUsageHandlerTests.cs` pattern almost verbatim (constructor sets up `Mock<ICatalogRepository>` + handler instance; each `[Fact]` arranges a fixture list, sets up `FindAsync`, calls `Handle`, asserts with `.Should()`).

No architectural risk, no new integration points, no design system touchpoints. This is squarely inside "Skip Design: true" territory per the review persona's own criteria (backend-only, no UI).

## Proposed Architecture

### Component Overview
No new components. One new test class is added to the existing test project, exercising the existing handler in isolation:

```
GetMaterialsForPurchaseHandlerTests (new)
        │
        ▼  Handle(request, CancellationToken.None)
GetMaterialsForPurchaseHandler (existing, unmodified)
        │
        ▼  FindAsync(predicate, ct)   [mocked]
Mock<ICatalogRepository>
        │
        ▼  ReturnsAsync(fixture IEnumerable<CatalogAggregate>)
Test fixture data (constructed inline, object initializers)
```

### Key Design Decisions

#### Decision 1: Fixture construction strategy
**Options considered:**
1. Construct `CatalogAggregate` via its full domain lifecycle (stock-taking methods, `AddPurchaseRecord`-style mutators) to mirror "real" object construction.
2. Construct `CatalogAggregate` via plain object initializer, setting only the properties the handler reads (`ProductCode`, `ProductName`, `Type`, `Stock`, `Location`, `PurchaseHistory`, `MinimalOrderQuantity`).

**Chosen approach:** Option 2 — plain object initializers, matching `GetProductUsageHandlerTests.CreateCatalogItem`.

**Rationale:** The handler is a pure read/project/filter over `CatalogAggregate` fields; it has no dependency on aggregate invariants enforced elsewhere (e.g., stock-taking business rules). Minimal fixtures keep tests focused on the three risk areas from the brief (search filter, price fallback, filter-then-limit ordering) without coupling test setup to unrelated aggregate behavior. `PurchaseHistory`'s setter running `UpdatePurchaseHistorySummary()` is a harmless side effect, not a reason to avoid direct initialization — the handler never reads `PurchaseHistorySummary`.

#### Decision 2: Repository mocking granularity
**Options considered:**
1. Mock `FindAsync` to actually evaluate the passed-in `Expression<Func<CatalogAggregate, bool>>` against a full fixture set (simulating real repository filtering behavior), so the `Material || Goods` predicate is genuinely exercised.
2. Mock `FindAsync` to ignore the predicate argument (`It.IsAny<...>()`) and directly return a pre-filtered fixture list that already represents "what the repository would have returned."

**Chosen approach:** Option 2, per the spec.

**Rationale:** The `Type == Material || Type == Goods` predicate is a one-line `Where` clause with no branching logic — evaluating it via expression-tree interpretation in the mock adds test complexity (would need `predicate.Compile()` and an `AsQueryable()`/`Where` step) for zero coverage value, since the brief's flagged risk areas are the *search term filter*, *price fallback*, and *filter-then-limit ordering* — not the type predicate. This matches the codebase's existing convention: `GetProductUsageHandlerTests` also uses `It.IsAny<...>()` for its repository setups rather than re-implementing filtering logic in test mocks.

## Implementation Guidance

### Directory / Module Structure
Single new file, no changes elsewhere:

```
backend/test/Anela.Heblo.Tests/Features/Catalog/
└── GetMaterialsForPurchaseHandlerTests.cs   [NEW]
```

Namespace: `Anela.Heblo.Tests.Features.Catalog` (matches sibling test files in the same directory, e.g. `GetProductUsageHandlerTests.cs`).

### Interfaces and Contracts
No new or modified interfaces. Test-relevant existing contracts (unchanged):

- `ICatalogRepository.FindAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken ct) : Task<IEnumerable<CatalogAggregate>>` — mock this.
- `GetMaterialsForPurchaseRequest { string? SearchTerm, int Limit }` — construct directly per test case.
- `GetMaterialsForPurchaseResponse { List<MaterialForPurchaseDto> Materials }` — assert against this.
- `MaterialForPurchaseDto { ProductCode, ProductName, ProductType, LastPurchasePrice, Location, CurrentStock, MinimalOrderQuantity }` — assert field-by-field per FR-2/FR-3/FR-5.

Class shape (mirrors `GetProductUsageHandlerTests`):
```csharp
public class GetMaterialsForPurchaseHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly GetMaterialsForPurchaseHandler _handler;

    public GetMaterialsForPurchaseHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _handler = new GetMaterialsForPurchaseHandler(_catalogRepositoryMock.Object);
    }

    // [Fact] per FR-2/FR-3/FR-4/FR-5 acceptance criterion, or
    // [Theory]+[InlineData] where multiple criteria share shape (e.g. the six
    // FR-2 search-match variants could collapse into one Theory covering
    // code-only/name-only/both/case-insensitive/no-match, keeping the empty-
    // SearchTerm case as its own [Fact] since its assertion shape differs).
}
```

Use a private `CreateCatalogItem(...)` fixture builder (matching `GetProductUsageHandlerTests.CreateCatalogItem`) parameterized at minimum by `productCode, productName, type, availableStock, location, purchaseHistory, minimalOrderQuantity` with sensible defaults, so each test only overrides what it's asserting.

### Data Flow
For every test case:
1. Build a `List<CatalogAggregate>` fixture representing exactly what the repository would return for the `Material || Goods` predicate (per Decision 2).
2. `_catalogRepositoryMock.Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(fixtures);`
3. Construct `GetMaterialsForPurchaseRequest { SearchTerm = ..., Limit = ... }`.
4. `var result = await _handler.Handle(request, CancellationToken.None);`
5. Assert on `result.Materials` — membership/count for filter+limit tests (FR-4, per the spec's own guidance to avoid brittle positional assertions except where `OrderBy(ProductName)` is explicitly under test), exact field values for mapping tests (FR-3, FR-5).

No other data flow paths exist — this is a single-hop mock-in/assert-out test, no async orchestration, no multi-step pipeline.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Positional `Take(Limit)` assertions become flaky/misleading because `Take` runs before the final `OrderBy(ProductName)` | Medium | Follow spec's FR-4 guidance exactly: for filter+limit tests, assert on `Count` and set-membership (`Should().OnlyContain(...)` / `Should().BeSubsetOf(...)`) rather than index position; reserve ordered/positional assertions for a dedicated test that specifically targets the final `OrderBy` |
| Coverage tool reports a different percentage than the 60% target due to line-counting nuances (e.g. object-initializer lines in the DTO projection) | Low | Non-blocking — acceptance criterion in spec FR-1 already frames "≥60%" as the target with FR-2–FR-5 branches as the actual proxy; if the number lands close but under, that's a spec/PR-review discussion, not an architectural concern |
| Mocking `FindAsync` with `It.IsAny<Expression<...>>()` means a future refactor that changes the predicate (e.g. adds a third eligible `ProductType`) won't be caught by these tests | Low | Explicitly out of scope per spec ("Out of Scope" section); acceptable since the brief didn't flag the predicate as a risk area and it has no branching logic |

## Specification Amendments
None. The spec is implementation-ready as written — FR-1 through FR-5 are unambiguous, acceptance criteria are concrete and independently verifiable, the "Data Model" section correctly anticipated and resolved the one open question about `PurchaseHistory`'s setter (confirmed during this review: it's a plain public setter), and "Out of Scope" appropriately excludes the type-predicate and validator work. Proceed as specified.

## Prerequisites
None. No migrations, no config, no infrastructure changes. The test project (`Anela.Heblo.Tests`) and its xUnit/Moq/FluentAssertions dependencies are already in place; the new file is a same-directory sibling of `GetProductUsageHandlerTests.cs` and requires no new project references.
