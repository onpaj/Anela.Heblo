# Specification: Unit test coverage for GetGiftPackageDetailHandler exception-to-error-code mapping

## Summary
`GetGiftPackageDetailHandler` catches two categories of exceptions from `IGiftPackageQueryService.GetGiftPackageDetailAsync` and maps them to two distinct `ErrorCodes`: `ArgumentException` → `ErrorCodes.ValidationError`, any other `Exception` → `ErrorCodes.InternalServerError`. This mapping is currently untested (12.5% line coverage, below the 60% threshold), so a code change that swaps or breaks the mapping would go undetected. This feature adds unit tests that lock in the correct mapping for both the success path and both exception paths.

## Background
The handler sits in the Logistics / GiftPackageManufacture vertical slice and is invoked via MediatR to fetch a single gift package's detail (composition, stock coverage, suggested quantity) for the UI. The frontend inspects `ErrorCode` on `GetGiftPackageDetailResponse` to decide whether to surface a validation message (e.g. "gift package code not found" from `ArgumentException`) or a generic server error banner (`InternalServerError`). A silent regression that swaps these codes — or that changes only one arm during a refactor — produces confusing UX (a real server failure reported as "invalid input", or vice versa) and slows down production triage, since `InternalServerError` is presumably surfaced/alerted differently than `ValidationError`. Other handlers in the same module (e.g. `DisassembleGiftPackageHandler`, see `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/DisassembleGiftPackageHandlerTests.cs`) already establish the test pattern this feature follows: a mocked service dependency, `Times.Once` verification, and one `[Fact]` per exception arm.

This is a pure test-coverage addition. No production code changes are required or in scope — the mapping being tested is already correct; the gap is the absence of a test that would catch a regression.

## Functional Requirements

### FR-1: Success path is covered
Add a test asserting that when `IGiftPackageQueryService.GetGiftPackageDetailAsync` returns a `GiftPackageDto` normally, the handler returns `Success = true`, `ErrorCode = null`, and `GiftPackage` populated with the DTO returned by the mocked service.

**Acceptance criteria:**
- Mock `IGiftPackageQueryService.GetGiftPackageDetailAsync` to return a representative `GiftPackageDto`.
- Assert `result.Success` is `true`.
- Assert `result.ErrorCode` is `null`.
- Assert `result.GiftPackage` is the same DTO (or has matching key fields, e.g. `Code`) returned by the mock.
- Verify the service method was called once with the request's parameters (`GiftPackageCode`, `SalesCoefficient`, `FromDate`, `ToDate`).

### FR-2: ArgumentException maps to ValidationError
Add a test asserting that when the service throws `ArgumentException`, the handler returns `Success = false` and `ErrorCode = ErrorCodes.ValidationError`.

**Acceptance criteria:**
- Mock `IGiftPackageQueryService.GetGiftPackageDetailAsync` to throw `new ArgumentException("...")` (use the single-argument constructor per the existing repo convention noted in `DisassembleGiftPackageHandlerTests.cs`, to avoid the two-argument ctor's `" (Parameter 'name')"` suffix affecting message assertions, if the message is asserted).
- Assert `result.Success` is `false`.
- Assert `result.ErrorCode` is `ErrorCodes.ValidationError`.
- Assert `result.GiftPackage` is `null`.

### FR-3: Non-ArgumentException maps to InternalServerError
Add a test asserting that when the service throws a generic exception (any type other than `ArgumentException`, e.g. `InvalidOperationException` or `Exception`), the handler returns `Success = false` and `ErrorCode = ErrorCodes.InternalServerError`.

**Acceptance criteria:**
- Mock `IGiftPackageQueryService.GetGiftPackageDetailAsync` to throw a non-`ArgumentException` (e.g. `new InvalidOperationException("...")`).
- Assert `result.Success` is `false`.
- Assert `result.ErrorCode` is `ErrorCodes.InternalServerError`.
- Assert `result.GiftPackage` is `null`.

### FR-4: The two error codes are distinct in the test suite
The tests for FR-2 and FR-3 together must make it impossible for a code swap (`ValidationError` ⇄ `InternalServerError` between the two catch blocks) to pass silently — i.e., each test must assert its own expected code exactly (not merely "is an error"), and the two expected values must differ.

**Acceptance criteria:**
- FR-2's assertion and FR-3's assertion reference different `ErrorCodes` enum members.
- Both tests fail if the handler's two catch blocks are swapped.

## Non-Functional Requirements

### NFR-1: Consistency with existing test conventions
Tests must follow the existing xUnit + Moq + FluentAssertions style used throughout `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/` (see `DisassembleGiftPackageHandlerTests.cs` as the closest sibling example): a `Mock<IGiftPackageQueryService>` field, `[Fact]` methods, Arrange/Act/Assert comments, `.Should()` assertions.

### NFR-2: No production code changes
The handler under test (`GetGiftPackageDetailHandler.cs`) is not modified. This is a test-only change.

### NFR-3: Coverage target
The new tests should be sufficient to raise line coverage of `GetGiftPackageDetailHandler.cs` from 12.5% to at or above the 60% filter threshold, by exercising both catch blocks and the success path (the only branches in the file).

## Data Model
No new data model. Existing types used as-is:
- `GetGiftPackageDetailRequest` — `GiftPackageCode: string`, `SalesCoefficient: decimal`, `FromDate: DateTime?`, `ToDate: DateTime?`.
- `GetGiftPackageDetailResponse : BaseResponse` — adds `GiftPackage: GiftPackageDto?`. `BaseResponse` supplies `Success`, `ErrorCode`, `Params`.
- `GiftPackageDto` (in `...GiftPackageManufacture.Contracts`) — used as the mocked service's return value; a minimal instance with `Code`/`Name` set is sufficient for the success-path test.
- `IGiftPackageQueryService.GetGiftPackageDetailAsync(string giftPackageCode, decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)` — the mocked dependency.
- `ErrorCodes.ValidationError` and `ErrorCodes.InternalServerError` — the two codes under test (`Anela.Heblo.Application.Shared`).

## API / Interface Design
No API surface changes. This is an internal unit test addition against the existing `IRequestHandler<GetGiftPackageDetailRequest, GetGiftPackageDetailResponse>` contract; no controller, route, or DTO changes.

## Dependencies
- xUnit, Moq, FluentAssertions (already referenced by `Anela.Heblo.Tests`).
- No new NuGet packages.
- No test data fixtures needed (docs/testing/test-data-fixtures.md fixtures are an E2E concept; this is a backend unit test with a fully mocked service, no real data dependency).

## Out of Scope
- Any change to `GetGiftPackageDetailHandler.cs` production logic.
- Testing `IGiftPackageQueryService`'s own implementation (that is covered separately, if at all, by its own tests).
- E2E or integration-level testing of this use case (this is unit-level, handler-only, per the issue's suggested approach).
- Testing other handlers in the same module (out of scope for this coverage-gap ticket).

## Open Questions

## Status: COMPLETE
