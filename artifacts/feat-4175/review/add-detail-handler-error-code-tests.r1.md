# Code Review: add-detail-handler-error-code-tests

## Summary
The new `GetGiftPackageDetailHandlerTests.cs` covers all three required scenarios (success, `ArgumentException` -> `ValidationError`, generic exception -> `InternalServerError`), each asserting its own exact error code against the other, which satisfies FR-4's swap-detection requirement. Implementation matches the task context verbatim, and the constructor/service signatures were verified against the current handler, request, response, DTO and service interface. No production code was touched. All three new tests pass; the full suite's 110 failures are pre-existing Testcontainers/Docker-dependent integration tests failing due to no Docker daemon in this environment, unrelated to this change.

## Review Result: PASS

### task: add-detail-handler-error-code-tests
**Status:** PASS

## Docs to Update
(None — this is a test-only change with no public behavior, CLI, or docs-relevant change.)

## Overall Notes
- FR-1 (success path): covered, including the `Times.Once` verification with exact argument values.
- FR-2 (ArgumentException -> ValidationError): covered, single-argument ctor used per convention.
- FR-3 (non-ArgumentException -> InternalServerError): covered, using `InvalidOperationException`.
- FR-4 (codes distinct / swap-detecting): each exception test asserts `Should().Be(...)` and `Should().NotBe(...)` on the other code — a catch-block swap would fail both tests.
- NFR-1 (style consistency): matches `DisassembleGiftPackageHandlerTests.cs` conventions (Mock field, `[Fact]`, Arrange/Act/Assert, FluentAssertions).
- NFR-2 (no production changes): confirmed — only the new test file was added.
- Build (`dotnet build Anela.Heblo.sln`) and `dotnet format --verify-no-changes` both pass with no errors and no formatting changes.
