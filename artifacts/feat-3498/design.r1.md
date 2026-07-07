# Design: Unit test coverage for `StockUpOperationResult`

## Component Design

No production components are added or modified — this is a single new test class added to the existing test tree, with zero wiring changes.

### `StockUpOperationResultTests` (new)
- **Location:** `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/StockUpOperationResultTests.cs`
- **Namespace:** `Anela.Heblo.Tests.Features.Catalog.Services` (matches sibling files `MarginCalculationServiceTests.cs`, `ProductWeightRecalculationServiceTests.cs`, `ProductCatalogQueryServiceTests.cs`, `SalesCostCalculationServiceTests.cs` in the same directory)
- **Framework:** xUnit (`[Fact]`, `[Theory]`) + FluentAssertions (`.Should()`), matching existing conventions in the same test project. No Moq — no dependencies to mock.
- **Responsibility:** Exercises the public surface of `StockUpOperationResult` (`Features/Catalog/Services/StockUpOperationResult.cs`) — its seven static factory methods and the `IsSuccess` computed property — without touching or duplicating production code.
- **Construction strategy:** `StockUpOperation` test instances are built directly via its public constructor (`documentNumber, productCode, amount, sourceType, sourceId`) using simple non-empty values (e.g. `"DOC-1"`, `"PROD-1"`, amount `1`) — no test builder/helper is introduced, since a single call site with two fixed arguments doesn't warrant one. Where a test needs `ErrorMessage` populated (the `PreviouslyFailed` case), it calls `operation.MarkAsFailed(timestamp, "some error")` after construction, since `ErrorMessage` has a private setter reachable only through that method.
- **Construction of `StockUpOperationResult` itself:** exclusively through its own public static factories (`Success`, `AlreadyCompleted`, `PreviouslyFailed`, `InProgress`, `AlreadyInShoptet`, `SubmitFailed`, `VerificationFailed`, `VerificationError`) — no reflection, no `Activator.CreateInstance` against the private parameterless constructor. Together these seven factories reach all six `StockUpResultStatus` values, which is what makes the `IsSuccess` predicate fully coverable without bypassing encapsulation.

### Test case shape
- One `[Fact]` per factory method (covers FR-2 through FR-9), each asserting in a single test body: `Status`, `Message` (exact literal or exact interpolated string — never re-derived), `Operation` (reference identity via `.Should().BeSameAs(operation)`), `Exception` (identity where applicable, else null), and `IsSuccess`.
- `InProgress` gets two `[Fact]`s (per FR-5): one with a non-null operation and a known `State`, one with `InProgress(null)` asserting `Operation` is `null` and `Message` equals the literal pinned string `"Operation already in progress (state: )"`.
- One additional dedicated test (table-driven `[Theory]` or equivalent) built from factory calls across all six `StockUpResultStatus` values, asserting `IsSuccess` true for `Success`/`AlreadyCompleted`/`AlreadyInShoptet` and false for `InProgress`/`PreviouslyFailed`/`Failed` — kept distinct from the per-factory tests so the "current set of success statuses" is pinned in one self-contained, readable place (FR-1).

No `.csproj` changes are needed — the test project uses default SDK-style globbing and already compiles sibling files from the same folder.

## Data Schemas

No new or changed data schemas, DTOs, or API contracts. This task only reads the existing shapes below to construct test fixtures and assertions:

- **`StockUpOperationResult`** (class, `Features/Catalog/Services/StockUpOperationResult.cs`) — properties: `Status` (`StockUpResultStatus`), `Message` (`string`, default `""`), `Operation` (`StockUpOperation?`), `Exception` (`Exception?`), computed `IsSuccess` (`bool`, true only for `Success`/`AlreadyCompleted`/`AlreadyInShoptet`).
- **`StockUpResultStatus`** (enum, same file) — `Success`, `AlreadyCompleted`, `AlreadyInShoptet`, `InProgress`, `PreviouslyFailed`, `Failed`.
- **`StockUpOperation`** (domain type, `Anela.Heblo.Domain.Features.Catalog.Stock`) — fields consumed by the factories under test: `ErrorMessage` (populated only via `MarkAsFailed(DateTime, string)`) and `State` (defaults to `StockUpOperationState.Pending` post-construction).

No API endpoints, controllers, or event payloads are introduced or affected.
