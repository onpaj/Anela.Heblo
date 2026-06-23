# Architecture Review: Unit Tests for ProductFilterService

## Skip Design: true

No UI components are involved. This is a pure backend test addition.

## Architectural Fit Assessment

The feature fits the existing Analytics test layer with no friction. `ProductFilterService` is already the same kind of stateless, dependency-free service as `MarginCalculator` and `ReportBuilderService`, both of which have test files following a simple pattern: instantiate the concrete class directly, build in-memory inputs with an object-initializer helper, invoke the method, assert with FluentAssertions.

The only structural difference is that `FilterProductsAsync` consumes an `IAsyncEnumerable<AnalyticsProduct>`. The project has no third-party async-enumerable testing helper, but C# async iterators (`async IAsyncEnumerable` methods decorated with `[AsyncStateMachine]`) are sufficient as test doubles — no mocking framework is needed.

Integration points:
- `Anela.Heblo.Domain.Features.Analytics.AnalyticsProduct` — the data type under test; used already in every existing Analytics test
- `Anela.Heblo.Application.Features.Analytics.Services.ProductFilterService` — the SUT; instantiated directly like `MarginCalculator` and `ReportBuilderService`
- `Anela.Heblo.Tests.csproj` — no new package references required; xUnit, FluentAssertions, and the domain/application project references are already present

## Proposed Architecture

### Component Overview

```
ProductFilterServiceTests                 (new test class)
  └── ProductFilterService                (SUT, instantiated directly as concrete type)
       ├── PassesFilters(product, ?, ?)   (sync, tested in FR-1..FR-3)
       └── FilterProductsAsync(stream, ?, ?, max, ct)  (async, tested in FR-4..FR-7)

Support:
  MakeProduct(name, category)             (private static helper)
  MakeStreamAsync(products[])             (private static async iterator helper)
```

There are no mocks, no substitutes, no DI container. `ProductFilterService` has no constructor parameters.

### Key Design Decisions

#### Decision 1: Concrete class vs interface instantiation
**Options considered:**
- Instantiate as `IProductFilterService` (tests against the interface contract)
- Instantiate as `ProductFilterService` (tests against the concrete type)

**Chosen approach:** Instantiate `ProductFilterService` directly, assigning to a `readonly` field of type `ProductFilterService`, exactly as `MarginCalculatorTests` does with `MarginCalculator`.

**Rationale:** Consistent with the established pattern. There is a single implementation and no swap scenario for these unit tests. The interface exists for DI wiring at the handler layer, not for test polymorphism.

#### Decision 2: Async stream test double
**Options considered:**
- NSubstitute or Moq stub for `IAsyncEnumerable<AnalyticsProduct>`
- Private `async IAsyncEnumerable` iterator method in the test class
- `System.Linq.Async` `ToAsyncEnumerable()` extension

**Chosen approach:** Private `async IAsyncEnumerable` iterator method (`MakeStreamAsync`), using C# language-level `yield return`.

**Rationale:** No new NuGet packages are permitted. NSubstitute can stub the interface but setting up `GetAsyncEnumerator` correctly is verbose. A simple iterator method is idiomatic, readable, and zero-dependency. `System.Linq.Async` is not referenced in the project.

#### Decision 3: Cancellation test approach
**Options considered:**
- Pass a pre-cancelled `CancellationToken` and assert `OperationCanceledException`
- Pass `CancellationToken.None` and verify the stream honours it structurally

**Chosen approach:** Pass an already-cancelled `CancellationToken` (`new CancellationToken(true)`) to `FilterProductsAsync` backed by a non-empty stream, and assert that `OperationCanceledException` (or its subtype `TaskCanceledException`) is thrown.

**Rationale:** `await foreach ... .WithCancellation(token)` throws when the token is already cancelled before or during the first `MoveNextAsync` call. This exercises the branch that wires `cancellationToken` into the `WithCancellation` call without requiring a timing-sensitive race. Use FluentAssertions `Awaiting(...).Should().ThrowAsync<OperationCanceledException>()`.

#### Decision 4: `maxProducts` boundary value
**Options considered:**
- Test only `maxProducts = 1` out of many products
- Test exact boundary: supply exactly N products, cap at N, verify no off-by-one

**Chosen approach:** Two sub-cases for FR-5: (a) stream has more items than `maxProducts` — verify the result is capped; (b) stream has fewer items than `maxProducts` — verify all are returned (confirms the `>=` guard does not over-cut). The exact boundary (stream count == maxProducts) can be covered by case (b) with count equal to limit.

**Rationale:** The `if (products.Count >= maxProducts) break;` guard has an off-by-one risk that is worth calling out explicitly.

## Implementation Guidance

### Directory / Module Structure

Place the new file at:

```
backend/test/Anela.Heblo.Tests/Features/Analytics/ProductFilterServiceTests.cs
```

This is the exact sibling of `MarginCalculatorTests.cs` and `ReportBuilderServiceTests.cs`.

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Tests.Features.Analytics;

public class ProductFilterServiceTests
{
    private readonly ProductFilterService _service = new();

    // Helper: build a minimal AnalyticsProduct with just name and category
    private static AnalyticsProduct MakeProduct(string name, string? category = null) => ...

    // Helper: async stream from a params array
    private static async IAsyncEnumerable<AnalyticsProduct> MakeStreamAsync(
        params AnalyticsProduct[] products) { ... }
}
```

**Test method naming** — follow the `{Method}_{Scenario}_{ExpectedOutcome}` convention visible in the existing tests (e.g. `PassesFilters_NullProductFilter_PassesNameCheck`, `FilterProductsAsync_ExceedsMaxProducts_CapsAtLimit`).

**Using directives required:**
```csharp
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Domain.Features.Analytics;
using FluentAssertions;
using Xunit;
```

### Data Flow

**`PassesFilters` tests (sync):**
1. Call `MakeProduct(name, category)` to produce an `AnalyticsProduct`.
2. Call `_service.PassesFilters(product, productFilter, categoryFilter)` directly.
3. Assert the `bool` return value.

**`FilterProductsAsync` tests (async):**
1. Build a `params` array of `AnalyticsProduct` values.
2. Pass through `MakeStreamAsync(...)` to get `IAsyncEnumerable<AnalyticsProduct>`.
3. `await _service.FilterProductsAsync(stream, productFilter, categoryFilter, maxProducts, ct)`.
4. Assert the returned `List<AnalyticsProduct>` (count, content, or exception).

No intermediate state, no shared mutable fields between tests.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `MakeStreamAsync` with `[EnumeratorCancellation]` not applied — cancellation test may pass trivially without actually exercising `.WithCancellation` | Medium | The test double does not need `[EnumeratorCancellation]`; the cancellation check in the production code fires inside `WithCancellation` on the outer enumerator, not inside the iterator. Use a pre-cancelled token to trigger it before the first item is fetched. Verify the test fails when `WithCancellation` is removed from the production code. |
| `ProductName` on `AnalyticsProduct` is `required` — forgetting it in `MakeProduct` causes a compile error | Low | The `MakeProduct` helper must supply `ProductCode`, `ProductName`, `Type`, `MarginAmount`, and `SalesHistory` (all `required`). Use `[]` for the empty `SalesHistory` as in existing tests. |
| Off-by-one in cap test: asserting `Count == maxProducts` when stream has exactly `maxProducts` items could mask a `>` vs `>=` bug | Medium | Include a case where stream has `maxProducts + 1` items to confirm the extra item is excluded. |
| Cancellation test flakiness if using a live `CancellationTokenSource.Cancel()` call concurrently | Low | Avoid races entirely: construct `new CancellationToken(canceled: true)` — already cancelled before the call. |

## Specification Amendments

**FR-7 precision:** The spec says "respects cancellation" without specifying the observable. Amend to: the method must throw `OperationCanceledException` (or `TaskCanceledException`) when called with a pre-cancelled token and a non-empty stream. This is the verifiable contract.

**FR-5 addition:** Add an explicit sub-case — stream item count equals `maxProducts` exactly — to confirm the boundary is inclusive and all items are returned (no premature cutoff from a `>` guard misreading).

No other spec changes required.

## Prerequisites

Everything required already exists:

- `ProductFilterService.cs` is present and compilable at `backend/src/Anela.Heblo.Application/Features/Analytics/Services/ProductFilterService.cs`
- `AnalyticsProduct` domain type is defined with all required properties
- `Anela.Heblo.Tests.csproj` already references `Anela.Heblo.Application`, `Anela.Heblo.Domain`, xUnit, and FluentAssertions
- No new NuGet packages, no new project references, no migration, no infrastructure change needed
