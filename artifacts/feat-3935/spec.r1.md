# Specification: Test Coverage for DeleteManufactureDifficultyHandler

## Summary
`DeleteManufactureDifficultyHandler` (Catalog module) currently has 23.7% line coverage, below the 60% CI threshold. This work adds a unit test suite that exercises the handler's three execution paths — not-found, happy-path delete-and-cache-refresh, and exception handling — without changing any production code.

## Background
The weekly coverage-gap routine flagged this handler on 2026-08-17 (CI run #31804633307). The handler deletes a `ManufactureDifficultySetting` record and then calls `ICatalogRepository.RefreshManufactureDifficultySettingsData` to keep the in-memory `CatalogAggregate` consistent with the persisted state. If the cache-refresh call is silently dropped, or invoked with the wrong `productCode`, the catalog aggregate would retain stale difficulty data, which downstream pricing calculations use as a coefficient — a silent data-quality regression that no other test currently guards against. This is a test-only task: no production code changes are expected or in scope.

## Functional Requirements

### FR-1: Not-found path returns failure without side effects
When `IManufactureDifficultyRepository.GetByIdAsync` returns `null` for the requested `Id`, the handler must return `Success = false` with a message containing the requested ID, and must not call `DeleteAsync` or `RefreshManufactureDifficultySettingsData`.

**Acceptance criteria:**
- `response.Success` is `false`.
- `response.Message` equals `"ManufactureDifficultyHistory with ID {id} not found"` for the given `Id`.
- `_repository.DeleteAsync(...)` is never invoked.
- `_catalogRepository.RefreshManufactureDifficultySettingsData(...)` is never invoked.

### FR-2: Happy path deletes and refreshes cache in the correct order with the correct product code
When the entry exists, the handler must call `DeleteAsync(request.Id, ...)` and then `RefreshManufactureDifficultySettingsData(existing.ProductCode, ...)`, and return `Success = true`.

**Acceptance criteria:**
- `_repository.DeleteAsync(request.Id, It.IsAny<CancellationToken>())` is called exactly once.
- `_catalogRepository.RefreshManufactureDifficultySettingsData(existing.ProductCode, It.IsAny<CancellationToken>())` is called exactly once, with the `ProductCode` taken from the entity returned by `GetByIdAsync` (not from the request, which carries no product code).
- The cache refresh call happens only after the delete call (sequencing via `MockSequence` or equivalent), confirmed with a distinct `ProductCode` value per test to make cross-wiring detectable.
- `response.Success` is `true`.
- `response.Message` equals `"Manufacture difficulty deleted successfully"`.

### FR-3: Exceptions from either repository call are caught and reported as failure
If `DeleteAsync` or `RefreshManufactureDifficultySettingsData` throws, the handler must catch the exception, return `Success = false` with a message that includes the exception's message, and must not let the exception propagate to the caller.

**Acceptance criteria:**
- Case A: `DeleteAsync` throws → `response.Success` is `false`; `response.Message` contains the thrown exception's `Message`; `RefreshManufactureDifficultySettingsData` is never called (since the throw happens before it in source order); no exception escapes `Handle`.
- Case B: `RefreshManufactureDifficultySettingsData` throws (after a successful `DeleteAsync`) → `response.Success` is `false`; `response.Message` contains the thrown exception's `Message`; no exception escapes `Handle`.

## Non-Functional Requirements

### NFR-1: Performance
N/A — unit tests only, no runtime behavior change. Tests must run in-memory with mocked dependencies (no I/O), consistent with sibling tests in the same folder.

### NFR-2: Security
N/A — no security-sensitive surface touched. No new dependencies introduced.

## Data Model
No schema changes. Relevant existing types (unchanged):
- `ManufactureDifficultySetting` (domain entity) — key fields used by tests: `Id` (int), `ProductCode` (string).
- `DeleteManufactureDifficultyRequest` — `Id` (int, required).
- `DeleteManufactureDifficultyResponse : BaseResponse` — `Message` (string?); inherits `Success` (bool) from `BaseResponse`. Note: this handler does not use `BaseResponse.ErrorCode` / `Params` — it only sets `Success` and `Message` directly, unlike sibling handlers (`UpdateManufactureDifficultyHandler`) in the same folder. Tests must assert on `Message`, not `ErrorCode`.

## API / Interface Design
No new or changed interfaces. Tests mock the two existing dependencies the handler already takes via constructor injection:
- `IManufactureDifficultyRepository` (`GetByIdAsync`, `DeleteAsync`)
- `ICatalogRepository` (`RefreshManufactureDifficultySettingsData`)
- `ILogger<DeleteManufactureDifficultyHandler>` (bare mock; log calls are not under test)

## Dependencies
- Existing test project `backend/test/Anela.Heblo.Tests` (xUnit + Moq + FluentAssertions), already referenced by sibling tests `CreateManufactureDifficultyHandlerTests.cs` and `UpdateManufactureDifficultyHandlerTests.cs` in `backend/test/Anela.Heblo.Tests/Features/Catalog/`. No new NuGet packages required.

## Out of Scope
- Any change to `DeleteManufactureDifficultyHandler.cs`, `DeleteManufactureDifficultyRequest.cs`, or `DeleteManufactureDifficultyResponse.cs` production code.
- Validator tests (there is no `DeleteManufactureDifficultyRequestValidator` in the repository at this path — only `Id` is `[Required]`, enforced by MVC model binding, not a FluentValidation validator).
- Integration/E2E coverage of the delete endpoint.
- Coverage of `IManufactureDifficultyRepository` or `ICatalogRepository` implementations themselves.

## Open Questions
None.

## Status: COMPLETE
