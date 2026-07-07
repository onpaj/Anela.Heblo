# Specification: Unit test coverage for `StockUpOperationResult`

## Summary
`backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpOperationResult.cs` currently has 0% line coverage. This work adds a focused unit test suite covering the `IsSuccess` computed property and all seven static factory methods (`Success`, `AlreadyCompleted`, `PreviouslyFailed`, `InProgress`, `AlreadyInShoptet`, `SubmitFailed`, `VerificationFailed`, `VerificationError`), pinning current behavior so future refactors of `StockUpResultStatus` or the factory methods are caught by regression tests.

## Background
`StockUpOperationResult` is a plain result-object class (not a record, per project convention) representing the outcome of a stock-up operation against Shoptet. Callers branch on `IsSuccess` to decide whether an operation succeeded. `IsSuccess` returns `true` for exactly three of the six `StockUpResultStatus` enum values (`Success`, `AlreadyCompleted`, `AlreadyInShoptet`) and `false` for the other three (`InProgress`, `PreviouslyFailed`, `Failed`). Because `IsSuccess` is an explicit allow-list rather than a deny-list, adding a new enum value in the future will silently default to `false` — this is very likely correct behavior, but currently unverified by any test. This is a coverage-gap remediation task (filed by the weekly coverage-gap routine on 2026-07-06), not a behavior change: no production code should be modified.

## Functional Requirements

### FR-1: `IsSuccess` predicate coverage
Add a parameterized (theory-style) test that exercises `IsSuccess` for every value currently defined in `StockUpResultStatus`.

**Acceptance criteria:**
- A test constructs (or otherwise obtains) a `StockUpOperationResult` with `Status` set to each of the six enum values: `Success`, `AlreadyCompleted`, `AlreadyInShoptet`, `InProgress`, `PreviouslyFailed`, `Failed`.
- Asserts `IsSuccess == true` for `Success`, `AlreadyCompleted`, `AlreadyInShoptet`.
- Asserts `IsSuccess == false` for `InProgress`, `PreviouslyFailed`, `Failed`.
- Because the class has a private parameterless constructor, tests must set `Status` either via one of the existing static factory methods (preferred, since it also validates factory behavior) or via object initializer syntax using the `init`-scoped `Status` property from within the test project (the constructor is private, but properties are `init`-settable, which C# permits via object initializers even with a private constructor called from a factory the type itself defines — note: the private constructor prevents `new StockUpOperationResult { ... }` from a test outside the class; see Open Questions).

### FR-2: `Success` factory method coverage
**Acceptance criteria:**
- Calling `StockUpOperationResult.Success(operation)` with a non-null `StockUpOperation` returns a result where:
  - `Status == StockUpResultStatus.Success`
  - `Message == "Stock up operation completed successfully"`
  - `Operation` is the same instance passed in
  - `Exception` is `null`
  - `IsSuccess == true`

### FR-3: `AlreadyCompleted` factory method coverage
**Acceptance criteria:**
- Calling `StockUpOperationResult.AlreadyCompleted(operation)` returns a result where:
  - `Status == StockUpResultStatus.AlreadyCompleted`
  - `Message == "Operation already completed"`
  - `Operation` is the same instance passed in
  - `Exception` is `null`
  - `IsSuccess == true`

### FR-4: `PreviouslyFailed` factory method coverage
**Acceptance criteria:**
- Calling `StockUpOperationResult.PreviouslyFailed(operation)` where `operation.ErrorMessage` is set to a known test string returns a result where:
  - `Status == StockUpResultStatus.PreviouslyFailed`
  - `Message == $"Operation previously failed: {operation.ErrorMessage}"` (verify the exact interpolated value appears in the message)
  - `Operation` is the same instance passed in
  - `Exception` is `null`
  - `IsSuccess == false`

### FR-5: `InProgress` factory method coverage
**Acceptance criteria:**
- Calling `StockUpOperationResult.InProgress(operation)` with a non-null `operation` whose `State` is set to a known value returns a result where:
  - `Status == StockUpResultStatus.InProgress`
  - `Message == $"Operation already in progress (state: {operation.State})"`
  - `Operation` is the same instance passed in
  - `Exception` is `null`
  - `IsSuccess == false`
- A second case calls `StockUpOperationResult.InProgress(null)` (the parameter is nullable) and asserts:
  - `Operation` is `null`
  - `Message` contains the rendered form of `null` state (i.e. `"Operation already in progress (state: )"`, since `operation?.State` on a null operation evaluates to `null` and interpolates as empty string) — the test should assert the literal expected string rather than re-deriving the interpolation logic, to genuinely pin behavior.
  - `IsSuccess == false`

### FR-6: `AlreadyInShoptet` factory method coverage
**Acceptance criteria:**
- Calling `StockUpOperationResult.AlreadyInShoptet(operation)` returns a result where:
  - `Status == StockUpResultStatus.AlreadyInShoptet`
  - `Message == "Document already exists in Shoptet history"`
  - `Operation` is the same instance passed in
  - `Exception` is `null`
  - `IsSuccess == true`

### FR-7: `SubmitFailed` factory method coverage
**Acceptance criteria:**
- Calling `StockUpOperationResult.SubmitFailed(operation, ex)` with a non-null `Exception` (e.g. `new InvalidOperationException("boom")`) returns a result where:
  - `Status == StockUpResultStatus.Failed`
  - `Message == $"Submit failed: {ex.Message}"`
  - `Operation` is the same instance passed in
  - `Exception` is the same instance passed in
  - `IsSuccess == false`

### FR-8: `VerificationFailed` factory method coverage
**Acceptance criteria:**
- Calling `StockUpOperationResult.VerificationFailed(operation)` returns a result where:
  - `Status == StockUpResultStatus.Failed`
  - `Message == "Verification failed: Record not found in Shoptet history after submission"`
  - `Operation` is the same instance passed in
  - `Exception` is `null`
  - `IsSuccess == false`

### FR-9: `VerificationError` factory method coverage
**Acceptance criteria:**
- Calling `StockUpOperationResult.VerificationError(operation, ex)` with a non-null `Exception` returns a result where:
  - `Status == StockUpResultStatus.Failed`
  - `Message == $"Verification error: {ex.Message}"`
  - `Operation` is the same instance passed in
  - `Exception` is the same instance passed in
  - `IsSuccess == false`

## Non-Functional Requirements

### NFR-1: Performance
N/A — pure unit tests over an in-memory value object; no I/O, no async, expected to run in milliseconds each.

### NFR-2: Security
N/A — no auth, no sensitive data, no external calls involved in this type or its tests.

### NFR-3: Test isolation and conventions
- Tests must not require a database, HTTP server, or any mocked infrastructure — `StockUpOperationResult` and `StockUpOperation` are plain in-memory types.
- Follow the existing test project conventions in the repository (test framework — likely xUnit based on typical .NET conventions in this repo; confirm by inspecting an existing test file in the same test project before authoring, e.g. under `backend/test/.../Catalog/` or equivalent).
- New test file should be named consistently with existing conventions, e.g. `StockUpOperationResultTests.cs`, placed in the test project mirroring the production namespace path `Features/Catalog/Services/`.
- Use a minimal valid `StockUpOperation` test fixture/builder (constructed directly or via existing test helpers if any exist in the codebase) rather than mocking, since it is a plain domain object.

## Data Model
No new or changed data model. Existing types referenced:
- `StockUpOperationResult` (class, `Features/Catalog/Services/StockUpOperationResult.cs`): properties `Status` (`StockUpResultStatus`), `Message` (`string`, defaults to `""`), `Operation` (`StockUpOperation?`), `Exception` (`Exception?`), and computed `IsSuccess` (`bool`).
- `StockUpResultStatus` (enum, same file): `Success`, `AlreadyCompleted`, `AlreadyInShoptet`, `InProgress`, `PreviouslyFailed`, `Failed`.
- `StockUpOperation` (domain type, `Anela.Heblo.Domain.Features.Catalog.Stock`): referenced fields used by the factories are `ErrorMessage` (used in `PreviouslyFailed`'s message) and `State` (used in `InProgress`'s message). Tests should inspect this type's actual definition before constructing instances, to use real property names and any required constructor arguments.

## API / Interface Design
N/A — this is a test-only change; no public API, controller, or UI surface is added or modified.

## Dependencies
- Existing test project for the `Anela.Heblo.Application` layer (locate the project covering `Features/Catalog/Services/*`; do not create a new test project if one already exists).
- `StockUpOperation` domain type (read-only dependency — must inspect its actual shape in `Anela.Heblo.Domain.Features.Catalog.Stock` to construct valid test instances; do not invent properties).
- Standard .NET test framework and assertion library already in use elsewhere in the test suite (match existing patterns — do not introduce a new test framework).

## Out of Scope
- Any change to production code in `StockUpOperationResult.cs` (this is a coverage-only task).
- Testing the callers/consumers of `StockUpOperationResult` (e.g. the service(s) that invoke these factory methods) — only the result type itself is in scope.
- Testing `StockUpOperation` itself, beyond what's needed to construct instances for these tests.
- Behavioral changes to `IsSuccess`, such as switching it from an allow-list to a deny-list, or adding new `StockUpResultStatus` values.

## Open Questions
- `StockUpOperationResult` has a `private` parameterless constructor, so it can only be instantiated via its own static factory methods from outside the class (object-initializer syntax like `new StockUpOperationResult { Status = ... }` is not accessible from the test project since the constructor is private). FR-1's `IsSuccess` coverage should therefore be validated indirectly through the existing factory methods, which between them exercise all six `StockUpResultStatus` values (`Success`→Success, `AlreadyCompleted`→AlreadyCompleted, `AlreadyInShoptet`→AlreadyInShoptet, `InProgress`→InProgress, `PreviouslyFailed`→PreviouslyFailed, `SubmitFailed`/`VerificationFailed`/`VerificationError`→Failed) rather than via direct object construction. Confirm this approach is acceptable, or confirm whether a test-internal factory/reflection-based approach is preferred — this spec assumes the factory-method-only approach and considers FR-1 satisfied by the combination of FR-2 through FR-9 assertions on `IsSuccess`, plus an explicit dedicated theory test for clarity and to make the "current set of success statuses" assertion self-contained and readable in one place.
- Exact test framework/assertion library in use in the target test project (xUnit/NUnit/MSTest, FluentAssertions or plain `Assert`) was not verified against the actual test project structure. The implementer should inspect a neighboring test file in the same project before authoring to match conventions exactly.

## Status: HAS_QUESTIONS
