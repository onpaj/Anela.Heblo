# Architecture Review: Unit test coverage for RecalculateProductWeightHandler

## Skip Design: true
This is a pure test-addition task. It creates one xUnit test file, changes no production code, adds no NuGet packages, and introduces no visual components, screens, or layouts. Confirmed by exploration: the unit under test is a MediatR handler whose only collaborator is an interface that will be mocked. There is nothing to design.

## Architectural Fit Assessment
The feature fits the existing conventions cleanly and requires no new architectural ground.

- The handler `RecalculateProductWeightHandler` (`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/RecalculateProductWeight/RecalculateProductWeightHandler.cs`) is a standard MediatR `IRequestHandler` with exactly two constructor dependencies: `IProductWeightRecalculationService` and `ILogger<RecalculateProductWeightHandler>`. Both are trivially mockable.
- The target test project `Anela.Heblo.Tests` already references xUnit + Moq + FluentAssertions and already hosts an established Catalog handler-test pattern. `CreateManufactureDifficultyHandlerTests.cs` is the canonical example: mocks constructed as `private readonly` fields, wired up in the constructor, system-under-test assembled once, `[Fact]`/`[Theory]` methods with Arrange/Act/Assert and FluentAssertions (`response.Success.Should().BeFalse()`).
- The main integration point is a single seam: the `IProductWeightRecalculationService` interface (`backend/src/Anela.Heblo.Application/Features/Catalog/Services/IProductWeightRecalculationService.cs`), which exposes `RecalculateAllProductWeights(ct)` and `RecalculateProductWeight(productCode, ct)`. This is exactly the seam the tests must control and verify, and it is a clean interface with no hidden state.

No conflict with existing patterns exists. The spec's proposed file path and namespace match the folder-per-module convention already in use.

## Proposed Architecture

### Component Overview
```
RecalculateProductWeightHandlerTests  (new, test project)
        │  constructs
        ▼
  RecalculateProductWeightHandler  (SUT, unchanged)
        │  depends on (both mocked)
        ├── Mock<IProductWeightRecalculationService>   ← Setup + Verify
        └── Mock<ILogger<RecalculateProductWeightHandler>>  ← passive, not asserted
        │  returns
        ▼
  RecalculateProductWeightResponse  ← FluentAssertions on fields
```

The service mock is the only behavioral collaborator. Tests drive its `Setup(...).ReturnsAsync(...)` / `.ThrowsAsync(...)` and confirm dispatch via `Verify(...)`. The logger mock is passed to satisfy the constructor and is deliberately not asserted (per spec Out of Scope).

### Key Design Decisions

#### Decision 1: Test the handler in isolation with a mocked service, not the real service
**Options considered:** (a) mock `IProductWeightRecalculationService`; (b) instantiate the real `ProductWeightRecalculationService` with mocked lower-level dependencies.
**Chosen approach:** Mock the interface (a).
**Rationale:** The coverage gap is the handler's dispatch and mapping logic, not the recalculation internals. Mocking the interface isolates exactly the reachable lines flagged (single-vs-all branch, mapping, `Success` derivation, catch block) and keeps tests fast and deterministic. Using the real service would pull in unrelated dependencies, blur what is under test, and violate the spec's Out of Scope boundary.

#### Decision 2: Assert dispatch with paired `Verify` (called-once + never-called)
**Options considered:** (a) assert only the response fields; (b) assert only that the expected method was called; (c) assert the expected method called `Times.Once` AND the sibling method `Times.Never`.
**Chosen approach:** (c) — the paired verification, on both branches.
**Rationale:** The brief identifies single-vs-all misrouting as the highest-risk regression (one product vs. full-catalog recalculation). Only the paired assertion pins the branch: a regression that routes to the wrong method is caught by the `Times.Never` half, which a response-only assertion would miss because both paths return a structurally identical `ProductWeightRecalculationResult`.

#### Decision 3: Parameterize the empty-input branch with `[Theory]`
**Options considered:** two separate `[Fact]`s for `null` and `""`; one `[Theory]` with `[InlineData(null)]` and `[InlineData("")]`.
**Chosen approach:** `[Theory]`.
**Rationale:** The handler branches on `string.IsNullOrEmpty`, so `null` and `""` are one equivalence class. A `[Theory]` documents that intent and avoids duplicated bodies, matching how neighboring tests express input variants.

## Implementation Guidance

### Directory / Module Structure
Create exactly one file:

- `backend/test/Anela.Heblo.Tests/Features/Catalog/RecalculateProductWeightHandlerTests.cs`
- Namespace: `Anela.Heblo.Tests.Features.Catalog`

No other files, no changes to `Anela.Heblo.Tests.csproj` (all dependencies already referenced), no production changes.

### Interfaces and Contracts
Verified concrete shapes the developer must code against (do not re-derive these):

- `RecalculateProductWeightRequest` — `public string? ProductCode { get; set; }`.
- `RecalculateProductWeightResponse : BaseResponse` — `ProcessedCount`, `SuccessCount`, `ErrorCount` (int), `ErrorMessages` (`List<string>`); `Success` is inherited from `BaseResponse` and **defaults to `true`**.
- `ProductWeightRecalculationResult` — `ProcessedCount`, `SuccessCount`, `ErrorCount` (int), `ErrorMessages` (`List<string>`), plus `Duration`/`StartTime`/`EndTime` (irrelevant to these tests).
- `IProductWeightRecalculationService`:
  - `Task<ProductWeightRecalculationResult> RecalculateAllProductWeights(CancellationToken ct = default)`
  - `Task<ProductWeightRecalculationResult> RecalculateProductWeight(string productCode, CancellationToken ct = default)`
- Handler signature under test: `Task<RecalculateProductWeightResponse> Handle(RecalculateProductWeightRequest, CancellationToken)`.

Test-class skeleton (mirror `CreateManufactureDifficultyHandlerTests`): `private readonly Mock<IProductWeightRecalculationService> _serviceMock;`, `private readonly Mock<ILogger<RecalculateProductWeightHandler>> _loggerMock;`, `private readonly RecalculateProductWeightHandler _handler;` all initialized in the constructor. Use `ReturnsAsync(...)` for success paths and `ThrowsAsync(new Exception("boom"))` for the fallback path.

Note on `Success`: because `BaseResponse.Success` defaults to `true`, the FR-3 negative case (`ErrorCount = 1 → Success == false`) is load-bearing — it proves the handler's `Success = result.ErrorCount == 0` line actually executed and flipped the default, rather than the assertion passing by accident. Keep that assertion explicit.

### Data Flow
1. **Single-product (FR-1):** request with `ProductCode = "PROD001"` → handler hits the `else` branch → calls `RecalculateProductWeight("PROD001", ct)` → maps result → response. Assert: `RecalculateProductWeight("PROD001", It.IsAny<CancellationToken>())` `Times.Once`; `RecalculateAllProductWeights(...)` `Times.Never`; response fields equal the stubbed result.
2. **Full-catalog (FR-2):** request with `ProductCode` null/empty → `if (string.IsNullOrEmpty(...))` true → calls `RecalculateAllProductWeights(ct)` → maps result → response. Assert: `RecalculateAllProductWeights` `Times.Once`; `RecalculateProductWeight` `Times.Never`.
3. **Success derivation (FR-3):** stub result `ErrorCount = 0` → `response.Success == true`; stub result `ErrorCount = 1` with non-empty `ErrorMessages` → `response.Success == false` and counts/messages passed through unchanged.
4. **Exception fallback (FR-4):** service mock throws → catch block returns `ProcessedCount == 0`, `SuccessCount == 0`, `ErrorCount == 1`, `Success == false`, `ErrorMessages` containing `"Internal error: boom"`. Assert the handler does not rethrow (the `await` returning a response is itself the proof).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Response-only assertions let a single-vs-all misroute pass undetected | Medium | Mandate paired `Times.Once` + `Times.Never` verification on both dispatch branches (Decision 2). |
| `Success` assertion passes by accident due to `BaseResponse` default `true` | Low | Require the FR-3 negative case (`ErrorCount = 1 → Success == false`) that forces the default to flip. |
| Over-testing logger interactions, coupling tests to implementation detail | Low | Follow spec Out of Scope: mock the logger but assert nothing on it. |
| Exception-path test asserts on exact string and later message wording changes | Low | Assert `ErrorMessages` contains a substring (`"Internal error"` / `"boom"`) rather than exact full-string equality. |
| Scope creep into validator or service internals | Low | Keep to the four FRs; validator and service internals are explicitly out of scope. |

## Specification Amendments
None required. The spec is accurate against the code: verified the handler's `string.IsNullOrEmpty` branch, the `Success = result.ErrorCount == 0` mapping, the catch block producing `"Internal error: {ex.Message}"` with `ErrorCount = 1`, and the exact member shapes of all four referenced types. One clarification worth carrying into implementation (already implied by FR-4's "at minimum on the branch reached by an empty ProductCode"): the `try/catch` wraps both branches, so a single exception test on either branch satisfies coverage of the catch block; testing both branches is optional polish, not required for the 60% target.

## Prerequisites
None. No migrations, config, infrastructure, or new packages. `Anela.Heblo.Tests` already references xUnit, Moq, and FluentAssertions, and the SUT plus all collaborator types already exist. Implementation can start immediately. Validate with `dotnet test` (and `dotnet format`) on `Anela.Heblo.Tests` per the project's completion checklist.
