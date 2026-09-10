# Implementation: setup-test-file

## What was implemented

Created the test file skeleton for `GetIssuedInvoiceSyncStatsHandler` unit tests: an empty
`GetIssuedInvoiceSyncStatsHandlerTests` class with the constructor wiring up the handler's
two dependencies (`IIssuedInvoiceRepository` mock and a null `ILogger`), exactly as specified
in the task context. No `[Fact]` test methods were added — those are covered by the follow-on
tasks (`date-defaulting-test`, `explicit-dates-test`, `exception-path-test`,
`happy-path-mapping-test`).

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` —
  new file containing the test class scaffold: a `Mock<IIssuedInvoiceRepository>` field and a
  `GetIssuedInvoiceSyncStatsHandler` field constructed in the test constructor with the mocked
  repository and a `Mock.Of<ILogger<GetIssuedInvoiceSyncStatsHandler>>()` logger.

## Tests

No test cases yet — this task only sets up the class scaffold (empty test class, 0 `[Fact]`
methods), matching the task-context's explicit acceptance criterion.

## How to verify

```
cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Ran this build: succeeded with `0 Errors` (249 pre-existing warnings in unrelated files,
none introduced by the new file).

## Notes

Followed the task-context code snippet verbatim. Verified the constructor shape against the
actual handler source (`GetIssuedInvoiceSyncStatsHandler.cs`): it takes exactly
`IIssuedInvoiceRepository` and `ILogger<GetIssuedInvoiceSyncStatsHandler>`, matching what the
task context specified. Confirmed naming/namespace conventions (xUnit, Moq, FluentAssertions,
`Anela.Heblo.Tests.Features.Invoices` namespace) against sibling test files in the same
directory (e.g. `GetRunningInvoiceImportJobsHandlerTests.cs`).

## PR Summary

Scaffolded the test class for `GetIssuedInvoiceSyncStatsHandler` so the follow-on tasks
(date-range defaulting, explicit dates, exception path, happy-path mapping) each have a
constructor and mocked repository/logger to append `[Fact]` methods to.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` — new empty test class with constructor-based mock setup

## Status
DONE
