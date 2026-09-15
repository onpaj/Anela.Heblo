# Design: Unit test coverage for DailyInvoiceImportJobBase

## Component Design

This feature has no user-facing component — it is a single new backend unit test file. No UX/UI sections apply.

### `DailyInvoiceImportJobBaseTests` (new)
- **Location:** `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Jobs/DailyInvoiceImportJobBaseTests.cs`
- **Namespace:** `Anela.Heblo.Tests.Features.Invoices.Infrastructure.Jobs`
- **Responsibility:** Exercise the three uncovered execution paths of `DailyInvoiceImportJobBase.ExecuteAsync` — job-disabled short-circuit, partial-failure warning branch, exception-rethrow branch — against mocked collaborators. Contains no assertions about currency-specific behavior (`DailyInvoiceImportEurJob`/`DailyInvoiceImportCzkJob` are out of scope; see spec's Out of Scope).
- **Collaborators (all mocked or null-op):**
  - `Mock<IInvoiceImportService>` — stands in for the real Shoptet/ABRA Flexi-backed import service; configured per-test to return an `ImportResultDto` or throw.
  - `Mock<IRecurringJobStatusChecker>` — configured per-test to return `true`/`false` for `IsJobEnabledAsync`.
  - `ILoggerFactory` — `NullLoggerFactory.Instance` for most tests; a `Mock<ILoggerFactory>` returning a `Mock<ILogger>` only in the one test that asserts a warning was logged.

### `TestDailyInvoiceImportJob` (new, private nested test double)
- **Location:** nested inside `DailyInvoiceImportJobBaseTests.cs` (not a standalone file — no new public type is introduced)
- **Responsibility:** The only concrete, instantiable stand-in for the abstract `DailyInvoiceImportJobBase` used by these tests. Supplies a fixed `Metadata` and `Currency` so the base class's constructor and abstract members are satisfiable in a test context, with no other behavior of its own.
- **Interface:** matches `DailyInvoiceImportJobBase`'s constructor signature exactly (`IInvoiceImportService`, `ILoggerFactory`, `IRecurringJobStatusChecker`), plus two extra constructor parameters (`jobName`, `currency`) so each test can name the job distinctly if useful (or a single fixed pair of constants is sufficient, following `BankImportJobBaseTests`'s `TestJobName`/`TestAccountName` constant pattern — implementer's choice, either is fine).

No other component changes. `DailyInvoiceImportJobBase`, `DailyInvoiceImportEurJob`, `DailyInvoiceImportCzkJob`, `IInvoiceImportService`, `IRecurringJobStatusChecker`, `ImportResultDto` are all read-only inputs to this design — none are modified.

## Data Schemas

No new or changed data schemas, database tables, API request/response shapes, or event payloads. The tests construct existing DTOs directly in memory:

```csharp
// A "some failed" result (FR-2 / partial-failure branch)
new ImportResultDto
{
    RequestId = "irrelevant-in-test",
    Succeeded = new List<string> { "INV-001" },
    Failed = new List<string> { "INV-002" }
}
```

No schema is persisted, transmitted, or serialized as part of this work — `ImportResultDto` is an existing in-process contract between `IInvoiceImportService` and its caller, unchanged by this task.
