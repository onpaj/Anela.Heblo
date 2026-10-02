# Specification: Coverage gap — FlexiBankStatementImportService failure/exception paths

## Summary
`FlexiBankStatementImportService.ImportStatementAsync` (backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Bank/FlexiBankStatementImportService.cs) sits at 29.2% line coverage against a 60% threshold. Only the happy path is exercised today; the explicit-failure branch, the null-error-message fallback, and the catch-all exception branch have no dedicated assertions. This is a test-only change: add a focused unit test class covering all outcome paths of `ImportStatementAsync`. No production code behavior changes.

## Background
Bank statement import is financial infrastructure: `ImportStatementAsync` is the boundary between the domain (`IBankStatementImportService`) and the FlexiBee ERP adapter. It wraps a call to `FlexiBankAccountClient.ImportStatementAsync` in a try/catch and normalizes every outcome into a `Result<bool>` (from `Anela.Heblo.Domain.Shared`):
- Success → `Result.Success(true)`
- FlexiBee-reported failure → `Result.Failure<bool>(flexiResult.ErrorMessage ?? "Unknown import error")`
- Any thrown exception → `Result.Failure<bool>($"Exception during import: {ex.Message}")`, exception swallowed (not rethrown)

A silent regression here (e.g. a refactor that returns `Result.Success` on the failure branch) would cause missing bank entries to go unnoticed by callers, since callers only inspect the `Result<bool>`, not logs. This is exactly the kind of contract that must be locked down with tests.

## Functional Requirements

### FR-1: Success path returns `Result.Success(true)`
When `_flexiBankAccountClient.ImportStatementAsync(accountId, statementData)` returns a `Result<bool>` with `IsSuccess == true`, `ImportStatementAsync` must return a `Result<bool>` where `IsSuccess == true` and `Value == true`.
**Acceptance criteria:**
- Given the mocked client returns success, the returned result's `IsSuccess` is `true`.
- The returned result's `Value` is `true`.

### FR-2: Explicit FlexiBee failure with a non-null error message is propagated
When the client's result has `IsSuccess == false` and a non-null `ErrorMessage`, `ImportStatementAsync` must return `Result.Failure<bool>` whose error/message equals that `ErrorMessage` verbatim.
**Acceptance criteria:**
- Given the mocked client returns a failure result with `ErrorMessage = "some FlexiBee error"`, the returned result's `IsSuccess` is `false`.
- The returned result's error text equals `"some FlexiBee error"` exactly (no wrapping/prefixing).

### FR-3: Explicit FlexiBee failure with a null error message falls back to a default message
When the client's result has `IsSuccess == false` and `ErrorMessage == null`, `ImportStatementAsync` must return `Result.Failure<bool>` whose error/message equals `"Unknown import error"`.
**Acceptance criteria:**
- Given the mocked client returns a failure result with `ErrorMessage = null`, the returned result's error text equals `"Unknown import error"`.

### FR-4: Exceptions thrown by the client are caught and converted to a failure result
When `_flexiBankAccountClient.ImportStatementAsync` throws any `Exception`, `ImportStatementAsync` must not let the exception propagate; it must return `Result.Failure<bool>` whose error/message equals `$"Exception during import: {ex.Message}"`.
**Acceptance criteria:**
- Given the mocked client throws `new InvalidOperationException("boom")` (or an equivalent test exception), calling `ImportStatementAsync` does not throw.
- The returned result's `IsSuccess` is `false`.
- The returned result's error text equals `"Exception during import: boom"`.

## Non-Functional Requirements

### NFR-1: Test isolation
Tests must not perform real network/FlexiBee calls. The FlexiBee-facing dependency is mocked at the test's chosen seam (see Dependencies / Open Questions — this is an architecture decision, not a product decision).

### NFR-2: No production behavior change
This is a coverage-only change. The `FlexiBankStatementImportService.ImportStatementAsync` method body must not be modified unless the architecture phase determines a minimal, purely mechanical change (e.g. marking a wrapped method `virtual`) is strictly required to make the class testable, in which case behavior must remain byte-for-byte identical.

## Data Model
No data model changes. Relevant existing types (read-only reference for the design/planning phases):
- `Result<bool>` / `Result` (`Anela.Heblo.Domain.Shared`) — outcome wrapper with `IsSuccess`, `Value`, and an error/message accessor; used via `Result.Success(true)` and `Result.Failure<bool>(string)`.
- `IBankStatementImportService.ImportStatementAsync(int accountId, string statementData)` (`Anela.Heblo.Domain.Features.Bank`) — the domain contract `FlexiBankStatementImportService` implements.
- `FlexiBankAccountClient.ImportStatementAsync(int accountId, string aboData)` — concrete adapter class, the service's direct collaborator; internally wraps the FlexiBee SDK's `IBankAccountClient.ImportStatement(accountId, aboData)` (from `Rem.FlexiBeeSDK.Client.Clients.BankAccounts`) and normalizes its result the same way.

## API / Interface Design
No API changes. Test-only addition:
- New test file `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Bank/FlexiBankStatementImportServiceTests.cs`, following this test project's established xUnit + Moq + FluentAssertions conventions (see e.g. `Stock/FlexiStockTakingDomainServiceTests.cs`, `Lots/FlexiLotsClientTests.cs`).

## Dependencies
- Test project `Anela.Heblo.Adapters.Flexi.Tests` already references Moq 4.20.70, FluentAssertions 6.12.0, xunit 2.5.3, and the `Anela.Heblo.Adapters.Flexi` project — no new package references expected.
- FlexiBee SDK package `Rem.FlexiBeeSDK.Client` (already referenced transitively) supplies `IBankAccountClient`, the interface actually exercised at the FlexiBee network boundary.

## Out of Scope
- Any change to `ImportStatementAsync`'s logic, return values, or logging behavior.
- Integration or end-to-end tests against a real/sandbox FlexiBee instance.
- Coverage of `FlexiBankAccountClient` itself beyond what naturally results from the chosen test seam (see Open Questions).
- Any other file's coverage.

## Open Questions
None — the one open design question (how to seam the test given `FlexiBankAccountClient` is a concrete class with a non-virtual `ImportStatementAsync`, so Moq cannot mock it directly) is an implementation-strategy decision for the architecture phase, not a product requirement; the functional requirements above (FR-1..FR-4) are unambiguous and testable regardless of which seam is chosen.

## Status: COMPLETE
