# Design: Unit test coverage for RecalculateProductWeightHandler

## Component Design

**`RecalculateProductWeightHandlerTests`** (new)
`backend/test/Anela.Heblo.Tests/Features/Catalog/RecalculateProductWeightHandlerTests.cs`, namespace `Anela.Heblo.Tests.Features.Catalog`.

- Mirrors the existing `CreateManufactureDifficultyHandlerTests` pattern: mocks and SUT built once in the constructor.
- Fields: `Mock<IProductWeightRecalculationService> _serviceMock`, `Mock<ILogger<RecalculateProductWeightHandler>> _loggerMock`, `RecalculateProductWeightHandler _handler`.
- Test methods (xUnit `[Fact]`/`[Theory]` + Moq + FluentAssertions):
  - Single-product dispatch (FR-1): stub `RecalculateProductWeight` success, verify it's called once and `RecalculateAllProductWeights` is never called; assert response mapping.
  - Full-catalog dispatch (FR-2): `[Theory]` with `[InlineData(null)]`/`[InlineData("")]`, verify `RecalculateAllProductWeights` called once and `RecalculateProductWeight` never called.
  - `Success` derivation (FR-3): `ErrorCount = 0` → `Success == true`; `ErrorCount = 1` with messages → `Success == false`, counts/messages pass through.
  - Exception fallback (FR-4): `_serviceMock` throws `Exception("boom")` on the empty-`ProductCode` branch (try/catch wraps both, so one branch suffices); assert handler doesn't rethrow and response is `ProcessedCount = 0`, `SuccessCount = 0`, `ErrorCount = 1`, `Success == false`, `ErrorMessages` containing `"Internal error: boom"`.

**Mocked collaborator: `IProductWeightRecalculationService`** (existing, unchanged)
`backend/src/Anela.Heblo.Application/Features/Catalog/Services/IProductWeightRecalculationService.cs`
- `Task<ProductWeightRecalculationResult> RecalculateAllProductWeights(CancellationToken ct = default)`
- `Task<ProductWeightRecalculationResult> RecalculateProductWeight(string productCode, CancellationToken ct = default)`

**SUT: `RecalculateProductWeightHandler`** (existing, unchanged) — no production code changes in this task.

No new NuGet packages; `Anela.Heblo.Tests` already references xUnit, Moq, and FluentAssertions.

## Data Schemas

N/A — no schema, DTO, or API changes. Existing types used as-is by the tests:

- `RecalculateProductWeightRequest` — `string? ProductCode`.
- `RecalculateProductWeightResponse : BaseResponse` — `ProcessedCount` (int), `SuccessCount` (int), `ErrorCount` (int), `ErrorMessages` (`List<string>`), `Success` (inherited, defaults to `true`).
- `ProductWeightRecalculationResult` — `ProcessedCount`, `SuccessCount`, `ErrorCount` (int), `ErrorMessages` (`List<string>`), plus `Duration`/`StartTime`/`EndTime` (not exercised by these tests).
