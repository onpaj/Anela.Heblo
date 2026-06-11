# Specification: Consolidate Bank Import Job Classes

## Summary

Eliminate ~180 lines of near-identical code across `ComgateCzkImportJob`, `ComgateEurImportJob`, and `ShoptetPayImportJob` by extracting a `BankImportJobBase` abstract class that owns the `ExecuteAsync` template (enabled check, log, dispatch, log, error catch). Each concrete job is reduced to its distinguishing metadata, account name, and date range. Hardcoded account name magic strings (`"ComgateCZK"`, `"ComgateEUR"`, `"ShoptetPay-CZK"`) are replaced with constants on a single `BankAccountNames` static class so a typo in a job class is a compile error.

## Background

`backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/` contains three jobs that all implement `IRecurringJob` and share an identical body:

1. Check `IRecurringJobStatusChecker.IsJobEnabledAsync` — early-return if disabled.
2. Log `"Starting {JobName}"`.
3. Build an `ImportBankStatementRequest(accountName, dateFrom, dateTo)`.
4. Send via `IMediator`.
5. Log `"{JobName} completed. Imported {Count} statements"`.
6. `catch (Exception ex)` → log error → rethrow.

The only differences between the three classes are:

| Class | `JobName` | Account name | Date range | Cron |
|---|---|---|---|---|
| `ComgateCzkImportJob` | `daily-comgate-czk-import` | `"ComgateCZK"` | `yesterday..yesterday` | `30 4 * * *` |
| `ComgateEurImportJob` | `daily-comgate-eur-import` | `"ComgateEUR"` | `yesterday..yesterday` | `40 4 * * *` |
| `ShoptetPayImportJob` | `daily-shoptetpay-czk-import` | `"ShoptetPay-CZK"` | `today..today` | `50 4 * * *` |

Any change to the shared body (cancellation-token plumbing, logging format, retry policy, instrumentation) has to be applied to three files. One of the three (`ShoptetPayImportJob`) already passes `cancellationToken` into `IsJobEnabledAsync`; the Comgate jobs do not — exactly the kind of silent drift the duplication encourages.

The hardcoded account name strings must match `BankAccountSettings.Accounts[].Name` in configuration. A rename in `appsettings.*.json` (or in Azure Key Vault) silently breaks the job at runtime — the failure surfaces as an `ArgumentException` from `ImportBankStatementHandler` when no matching account is found. There is already drift in the codebase: local `appsettings.json` defines accounts named `AccountCZK` / `AccountEUR`, while the jobs reference `ComgateCZK` / `ComgateEUR` (production/Key Vault overrides presumably realign this — but the contract is invisible to the compiler).

The registration plumbing (`AddRecurringJobs` in `ServiceCollectionExtensions.cs` — assembly scan for non-abstract `IRecurringJob` types — and `HangfireJobRegistrationHelper.RegisterOrUpdate(jobType, ...)` — `MakeGenericMethod(jobType)`) keys off the concrete CLR type. An abstract base class is skipped by the scan automatically; three concrete subclasses remain distinct Hangfire jobs without any plumbing changes. A "single parameterised class with three DI registrations" approach would not work without modifying that plumbing and is therefore out of scope.

## Functional Requirements

### FR-1: Introduce `BankImportJobBase` abstract class

Add `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportJobBase.cs` containing the shared `ExecuteAsync` template, the three injected dependencies (`IMediator`, `ILogger`, `IRecurringJobStatusChecker`), and a single abstract method exposing the per-job parameters.

The base class:

- Implements `IRecurringJob`.
- Is `abstract`.
- Declares `public abstract RecurringJobMetadata Metadata { get; }` (overridden per subclass).
- Holds `IMediator`, `ILogger<BankImportJobBase>`, and `IRecurringJobStatusChecker` as `protected` fields, populated through a single protected constructor with `ArgumentNullException.ThrowIfNull` guards (matching the existing pattern).
- Declares `protected abstract BankImportRequest BuildRequest();` (or equivalent — see FR-2) that the subclass implements to return the account name + date range for this run.
- Sealed `ExecuteAsync(CancellationToken)` (or `virtual` but not overridden in this scope) that performs the exact same six-step flow currently duplicated, with `cancellationToken` propagated to **both** `IsJobEnabledAsync` and `IMediator.Send` (closes the existing drift in the Comgate jobs).

**Acceptance criteria:**

- `BankImportJobBase` is declared `abstract` (so it is not picked up by the `AddRecurringJobs` assembly scan; verified by enumerating registered jobs in a test).
- `ExecuteAsync` is implemented exactly once in the base class. The three subclasses contain no `try/catch`, no `_statusChecker` call, no `_mediator.Send`, and no logging statements (other than what the framework injects).
- The `cancellationToken` parameter of `ExecuteAsync` is passed to `IsJobEnabledAsync` and to `IMediator.Send`. The current `ComgateCzk/Eur` jobs drop it on `IsJobEnabledAsync`; after this refactor, all three jobs propagate it.
- All three log messages (`"Starting {JobName}"`, `"{JobName} completed. Imported {Count} statements"`, `"{JobName} failed"`) remain byte-identical to the existing format so downstream log-based alerting/searches do not break.
- The `catch` block continues to log + rethrow (preserves Hangfire retry behavior).

### FR-2: Subclass shape — date and account passed via abstract members

Each subclass overrides `Metadata` and exposes its account name + date range. There are two acceptable shapes; the chosen shape is **abstract method returning a value object**:

```csharp
protected abstract BankImportJobParameters GetParameters();
```

where `BankImportJobParameters` is an internal `record`-style value type holding `AccountName`, `DateFrom`, `DateTo`. Returning a value object (rather than three separate abstract properties) keeps subclasses to two overrides and lets the base class call `GetParameters()` exactly once per invocation — avoiding the subtle bug of calling `DateTime.Today` twice during a midnight rollover.

The base class then builds the request:

```csharp
var p = GetParameters();
var request = new ImportBankStatementRequest(p.AccountName, p.DateFrom, p.DateTo);
```

Subclasses use a `static readonly` `BankAccountNames` constant (FR-3) for the account name and compute the date inline (`DateTime.Today.AddDays(-1)` / `DateTime.Today`). `DateTime.Today` is acceptable as-is — the existing jobs use it, and changing the clock-source is out of scope.

**Acceptance criteria:**

- Each concrete job class has exactly: metadata, constructor (forwarding all dependencies to `: base(...)`), and a single `GetParameters()` override.
- Each concrete job class is ≤ 30 lines (down from ~60).
- `GetParameters()` is called exactly once per `ExecuteAsync` invocation (verified by a base-class test that overrides `GetParameters` with a counter).

### FR-3: Introduce `BankAccountNames` constants

Add `backend/src/Anela.Heblo.Application/Features/Bank/BankAccountNames.cs` (or under `Infrastructure/` — the architect chooses placement based on `docs/architecture/filesystem.md`) with `public const string` members:

```csharp
public static class BankAccountNames
{
    public const string ComgateCzk = "ComgateCZK";
    public const string ComgateEur = "ComgateEUR";
    public const string ShoptetPayCzk = "ShoptetPay-CZK";
}
```

Each job class references the matching constant; the literal strings are no longer present in the job source files. Values must match exactly the strings currently hardcoded in the jobs — these are wire/contract values consumed by `ImportBankStatementHandler` to look up `BankAccountSettings.Accounts[].Name`.

**Acceptance criteria:**

- `grep -r '"ComgateCZK"' backend/src/Anela.Heblo.Application/Features/Bank/` returns only the constant definition.
- Same for `"ComgateEUR"` and `"ShoptetPay-CZK"`.
- A typo in a job (e.g., `BankAccountNames.ComgaetCzk`) fails to compile.
- Constant values are byte-identical to the prior hardcoded strings (no functional behavior change).

### FR-4: Preserve all observable behavior

The refactor is a structural change only — no behavior change beyond the corrective propagation of `cancellationToken` (FR-1).

**Acceptance criteria:**

- `JobName`, `DisplayName`, `Description`, `CronExpression`, `DefaultIsEnabled`, and `TimeZoneId` (defaulted to `Europe/Prague` by `RecurringJobMetadata`) on each subclass remain byte-identical to the values in the current source.
- Each Hangfire recurring job continues to be discovered (verified via the `RecurringJobDiscoveryService` log output: `"Successfully registered N recurring jobs"` count is unchanged).
- The `ImportBankStatementRequest` payload (account name, dateFrom, dateTo) for each job is identical to current behavior on any given day.
- The log messages emitted on a successful run match the existing format exactly: `"Starting {JobName}"`, `"{JobName} completed. Imported {Count} statements"`, `"{JobName} failed"`.

### FR-5: Test coverage

Replace and expand the existing test surface. (Confirm whether `ComgateCzk/Eur/ShoptetPay` job tests exist today — the Grep result indicates no `Bank/Infrastructure/Jobs/*Tests.cs` file yet, only handler-level `ImportBankStatementHandlerTests`. If absent, this requirement adds the first job-level coverage.)

**Required tests:**

1. **Base-class template tests** (`BankImportJobBaseTests`): use a minimal test double subclass of `BankImportJobBase` to verify, with mocked `IMediator` and `IRecurringJobStatusChecker`:
   - `ExecuteAsync` returns early without calling `IMediator.Send` when `IsJobEnabledAsync` returns `false`.
   - `ExecuteAsync` calls `IMediator.Send` exactly once with an `ImportBankStatementRequest` whose `AccountName`, `DateFrom`, `DateTo` match the values returned by the test double's `GetParameters()`.
   - `ExecuteAsync` forwards the `CancellationToken` to both `IsJobEnabledAsync` and `IMediator.Send`.
   - When `IMediator.Send` throws, `ExecuteAsync` logs at `Error` level with the job name and rethrows the original exception type.
   - `GetParameters()` is called exactly once per invocation.

2. **Per-job parameter tests**: one test per concrete job (`ComgateCzkImportJobTests`, `ComgateEurImportJobTests`, `ShoptetPayImportJobTests`) verifying the `GetParameters()` output: account name constant and date offset (`yesterday` or `today`). Inject a `TimeProvider` if the team wishes to make the date deterministic; otherwise allow ±1 day tolerance in the assertion to stay deterministic across midnight boundaries.

3. **Discovery test**: an integration- or service-collection-level test asserting that `AddRecurringJobs()` registers exactly three Bank import jobs (and that the abstract base class is **not** present in `GetServices<IRecurringJob>()`).

**Acceptance criteria:**

- All tests pass against the refactored code and fail (compile or assertion) against the pre-refactor code.
- Coverage of `BankImportJobBase.ExecuteAsync` is ≥ 90% line coverage (it is the only behavioral surface).
- `dotnet build` and `dotnet format` succeed.

### FR-6: Delete obsolete duplicate bodies

After the subclass refactor, the three files retain only the constructor + `Metadata` override + `GetParameters` override. No `_mediator`, `_logger`, or `_statusChecker` fields. No `using` directives beyond what the slim file needs.

**Acceptance criteria:**

- Each of the three job files is ≤ 30 lines (down from 59–60).
- No copy-pasted `ExecuteAsync` body remains in any concrete job class.

## Non-Functional Requirements

### NFR-1: Performance

No measurable change. The added virtual dispatch on `GetParameters()` is one extra method call per job execution per day, on a code path that already issues HTTP calls to bank APIs — performance impact is negligible and not worth measuring.

### NFR-2: Security

No new attack surface. Account names remain non-secret configuration keys (the actual bank credentials live in Key Vault, accessed via `BankClientFactory`). Constants in source code expose no information not already present in the existing job files.

### NFR-3: Backwards compatibility

- **Hangfire job storage** is keyed on the job name string (`daily-comgate-czk-import`, etc.) and the concrete CLR type. Job names are unchanged. The CLR types are unchanged (`ComgateCzkImportJob`, `ComgateEurImportJob`, `ShoptetPayImportJob` keep their names and namespaces — they merely inherit from the new base). Existing Hangfire recurring-job rows continue to bind without any database migration or one-time cleanup script.
- The DB-stored cron-expression overrides (`recurring_job_configurations` table, read in `RecurringJobDiscoveryService.StartAsync`) continue to apply: they are keyed by `JobName`, which is unchanged.
- No appsettings/Key Vault change is required.

### NFR-4: Maintainability

The driving NFR. Future changes to the import flow (logging format, retry annotations, OpenTelemetry instrumentation, cancellation-token handling, structured error codes) touch one file instead of three.

## Data Model

No persistent data model changes.

**New ephemeral type:**

```csharp
internal sealed class BankImportJobParameters
{
    public required string AccountName { get; init; }
    public required DateTime DateFrom { get; init; }
    public required DateTime DateTo { get; init; }
}
```

A class (not a `record`) per the project's "DTOs are classes" rule — although this is a purely internal value type (not exposed via OpenAPI), staying consistent with the project default avoids accidental record-typed leakage to a future contract. The architect may choose `record` if confident the type stays internal-only.

## API / Interface Design

No HTTP/API surface change.

**New internal interface contract:**

`BankImportJobBase` exposes one extension point to subclasses:

```csharp
protected abstract BankImportJobParameters GetParameters();
```

**Inheritance graph (new):**

```
IRecurringJob
    └── BankImportJobBase (abstract)
            ├── ComgateCzkImportJob
            ├── ComgateEurImportJob
            └── ShoptetPayImportJob
```

**File layout (new + modified):**

```
backend/src/Anela.Heblo.Application/Features/Bank/
├── BankAccountNames.cs                              [NEW]
└── Infrastructure/
    └── Jobs/
        ├── BankImportJobBase.cs                     [NEW]
        ├── BankImportJobParameters.cs               [NEW]
        ├── ComgateCzkImportJob.cs                   [MODIFIED — reduced]
        ├── ComgateEurImportJob.cs                   [MODIFIED — reduced]
        └── ShoptetPayImportJob.cs                   [MODIFIED — reduced]

backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/
├── BankImportJobBaseTests.cs                        [NEW]
├── ComgateCzkImportJobTests.cs                      [NEW]
├── ComgateEurImportJobTests.cs                      [NEW]
└── ShoptetPayImportJobTests.cs                      [NEW]
```

## Dependencies

- `MediatR` — already referenced by all three jobs.
- `Microsoft.Extensions.Logging.Abstractions` — already referenced.
- `Anela.Heblo.Domain.Features.BackgroundJobs` (`IRecurringJob`, `IRecurringJobStatusChecker`, `RecurringJobMetadata`) — already referenced.
- `Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement` (`ImportBankStatementRequest`) — already referenced.
- **Test:** `xUnit`, `FluentAssertions`, `Moq` — already in `backend/test/Anela.Heblo.Tests/`.

No new NuGet packages.

## Out of Scope

- Changes to `ImportBankStatementHandler` or `ImportBankStatementRequest`.
- Changes to `BankAccountSettings` / `BankAccountConfiguration` shape or the `BankAccounts` config section structure.
- Reconciling the `AccountCZK`/`AccountEUR` (local dev `appsettings.json`) vs `ComgateCZK`/`ComgateEUR` (jobs) drift — that is a separate config-correctness investigation. This refactor preserves the existing hardcoded strings verbatim under new constant names.
- Adding a startup-time validator that asserts every `BankAccountNames` constant matches some `BankAccountSettings.Accounts[].Name`. Worthwhile but a separate change.
- Introducing `TimeProvider` / `IClock` to make job dates deterministic in tests. The existing jobs use `DateTime.Today` directly; this refactor preserves that. A future change can inject a clock.
- Changes to the cron schedules, time zone, or `DefaultIsEnabled` values.
- Refactoring other recurring jobs (e.g., `DailyInvoiceImportCzkJob`, `DailyInvoiceImportEurJob`) that may exhibit similar duplication. Each module's refactor is its own scope.
- Changes to `HangfireJobRegistrationHelper`, `RecurringJobDiscoveryService`, or `AddRecurringJobs`. The chosen design works with the existing plumbing untouched.

## Open Questions

None.

## Status: COMPLETE