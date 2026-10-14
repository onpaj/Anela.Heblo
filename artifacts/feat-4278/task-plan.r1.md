# ImportBankStatementRequest Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Pipeline note:** This plan runs in a fully automated pipeline. Each `### task:` section below is a self-contained unit of work with its own commit. Task headers use the `### task: <task-name>` form (not `### Task N: <name>`) so the planning-stage orchestrator can extract them mechanically — this is a deliberate deviation from the general writing-plans task-header convention, required by this pipeline's task-extraction step.

**Goal:** Add a FluentValidation validator for `ImportBankStatementRequest` so an unknown/empty `AccountName` or an inverted `DateFrom`/`DateTo` range returns HTTP 400 instead of HTTP 500, and remove the now-redundant manual `ArgumentException` from `ImportBankStatementHandler`.

**Architecture:** Add `ImportBankStatementRequestValidator : AbstractValidator<ImportBankStatementRequest>` in `Features/Bank/Validators/`, register it plus a matching `ValidationBehavior<ImportBankStatementRequest, BankStatementImportResultDto>` pipeline behavior in `BankModule.AddBankModule` (the exact pattern already used for `GetBankStatementListRequestValidator`), then delete the handler's manual account-not-found guard since validation now guarantees the account exists before `Handle` runs.

**Tech Stack:** .NET 8, MediatR, FluentValidation (`AbstractValidator<T>`, `FluentValidation.TestHelper`), xUnit, Moq, FluentAssertions.

---

### task: add-import-bank-statement-validator

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Bank/Validators/ImportBankStatementRequestValidator.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Bank/Validators/ImportBankStatementRequestValidatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Bank/Validators/ImportBankStatementRequestValidatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Application.Features.Bank.Validators;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using FluentValidation.TestHelper;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank.Validators;

public class ImportBankStatementRequestValidatorTests
{
    private readonly ImportBankStatementRequestValidator _validator;

    public ImportBankStatementRequestValidatorTests()
    {
        var settings = new BankAccountSettings
        {
            Accounts = new List<BankAccountConfiguration>
            {
                new BankAccountConfiguration
                {
                    Name = "ComgateCZK",
                    Provider = BankClientProvider.Comgate,
                    AccountNumber = "123456789",
                    FlexiBeeId = 1,
                    Currency = CurrencyCode.CZK
                }
            }
        };

        _validator = new ImportBankStatementRequestValidator(Options.Create(settings));
    }

    [Fact]
    public void AccountName_Known_ShouldNotHaveValidationError()
    {
        var request = new ImportBankStatementRequest("ComgateCZK", DateTime.Today.AddDays(-1), DateTime.Today);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.AccountName);
    }

    [Fact]
    public void AccountName_Unknown_ShouldHaveValidationError()
    {
        var request = new ImportBankStatementRequest("UNKNOWN", DateTime.Today.AddDays(-1), DateTime.Today);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.AccountName);
    }

    [Fact]
    public void AccountName_Empty_ShouldHaveValidationError()
    {
        var request = new ImportBankStatementRequest("", DateTime.Today.AddDays(-1), DateTime.Today);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.AccountName);
    }

    [Fact]
    public void DateFrom_BeforeDateTo_ShouldNotHaveValidationError()
    {
        var request = new ImportBankStatementRequest("ComgateCZK", new DateTime(2024, 1, 1), new DateTime(2024, 1, 31));

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.DateFrom);
    }

    [Fact]
    public void DateFrom_EqualToDateTo_ShouldNotHaveValidationError()
    {
        var sameDate = new DateTime(2024, 1, 15);
        var request = new ImportBankStatementRequest("ComgateCZK", sameDate, sameDate);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.DateFrom);
    }

    [Fact]
    public void DateFrom_AfterDateTo_ShouldHaveValidationError()
    {
        var request = new ImportBankStatementRequest("ComgateCZK", new DateTime(2024, 1, 31), new DateTime(2024, 1, 1));

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.DateFrom)
            .WithErrorMessage("DateFrom must not be later than DateTo");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (validator class does not exist yet)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportBankStatementRequestValidatorTests"`
Expected: FAIL to compile — `ImportBankStatementRequestValidator` does not exist in `Anela.Heblo.Application.Features.Bank.Validators`.

- [ ] **Step 3: Write minimal implementation**

Create `backend/src/Anela.Heblo.Application/Features/Bank/Validators/ImportBankStatementRequestValidator.cs`:

```csharp
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.Bank;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Bank.Validators;

public class ImportBankStatementRequestValidator : AbstractValidator<ImportBankStatementRequest>
{
    public ImportBankStatementRequestValidator(IOptions<BankAccountSettings> bankSettings)
    {
        var settings = bankSettings.Value;

        RuleFor(x => x.AccountName)
            .NotEmpty().WithMessage("AccountName is required")
            .Must(name => settings.Accounts.Any(a => a.Name == name))
            .WithMessage(x => $"Account name {x.AccountName} not found in {BankAccountSettings.ConfigurationKey} configuration.")
            .When(x => !string.IsNullOrEmpty(x.AccountName));

        RuleFor(x => x.DateFrom)
            .Must((req, dateFrom) => dateFrom.Date <= req.DateTo.Date)
            .WithMessage("DateFrom must not be later than DateTo");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportBankStatementRequestValidatorTests"`
Expected: PASS — all 6 tests green.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/Validators/ImportBankStatementRequestValidator.cs backend/test/Anela.Heblo.Tests/Features/Bank/Validators/ImportBankStatementRequestValidatorTests.cs
git commit -m "feat(bank): add ImportBankStatementRequestValidator"
```

---

### task: register-import-bank-statement-validator-in-di

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs:29-32`

- [ ] **Step 1: Confirm current DI registration block**

The relevant existing lines in `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` read:

```csharp
        services.AddScoped<IValidator<GetBankStatementListRequest>, GetBankStatementListRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetBankStatementListRequest, GetBankStatementListResponse>,
            ValidationBehavior<GetBankStatementListRequest, GetBankStatementListResponse>>();
```

- [ ] **Step 2: Add the new registrations directly below that block**

Edit `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` so the block becomes:

```csharp
        services.AddScoped<IValidator<GetBankStatementListRequest>, GetBankStatementListRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetBankStatementListRequest, GetBankStatementListResponse>,
            ValidationBehavior<GetBankStatementListRequest, GetBankStatementListResponse>>();

        services.AddScoped<IValidator<ImportBankStatementRequest>, ImportBankStatementRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<ImportBankStatementRequest, BankStatementImportResultDto>,
            ValidationBehavior<ImportBankStatementRequest, BankStatementImportResultDto>>();
```

Add the two missing `using` statements at the top of the file (both namespaces already exist in the codebase, and `BankModule.cs` already has a `using Anela.Heblo.Application.Features.Bank.Validators;` line — verify it's present, it already is since `GetBankStatementListRequestValidator` is in that namespace):

```csharp
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Application.Features.Bank.Contracts;
```

(`ImportBankStatementRequest` lives in `Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement`; `BankStatementImportResultDto` lives in `Anela.Heblo.Application.Features.Bank.Contracts` — add whichever of these two `using` lines is not already present in the file. `Anela.Heblo.Application.Features.Bank.Validators` is already imported since `GetBankStatementListRequestValidator` is used two lines above.)

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, no errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs
git commit -m "feat(bank): register ImportBankStatementRequestValidator in DI pipeline"
```

---

### task: remove-redundant-argumentexception-from-import-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs:50-61`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs:88-97`

- [ ] **Step 1: Update the handler test file first — remove the now-invalid test**

In `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`, delete this test (the handler will no longer throw `ArgumentException` for an unknown account — that responsibility now belongs to `ImportBankStatementRequestValidator`, already covered by `ImportBankStatementRequestValidatorTests.AccountName_Unknown_ShouldHaveValidationError` from the previous task):

```csharp
    [Fact]
    public async Task Handle_WithUnknownAccount_ThrowsArgumentException()
    {
        var request = new ImportBankStatementRequest("UNKNOWN", DateTime.Today, DateTime.Today);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.Handle(request, CancellationToken.None));

        Assert.Contains("Account name UNKNOWN not found", exception.Message);
    }
```

Do not remove any other test in this file — `Constructor_WithNullFactory_ThrowsArgumentNullException` and `Handle_WithValidAccount_ResolvesClientViaFactory` (and any tests below them) stay exactly as they are.

- [ ] **Step 2: Run the handler tests to confirm the suite still compiles and the rest still passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportBankStatementHandlerTests"`
Expected: PASS — all remaining tests green (the deleted test is simply gone from the run).

- [ ] **Step 3: Remove the handler's manual `ArgumentException` guard**

In `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`, replace:

```csharp
        var accountSetting = _bankSettings.Accounts.SingleOrDefault(a => a.Name == request.AccountName);
        if (accountSetting == null)
        {
            var availableAccounts = string.Join(", ", _bankSettings.Accounts.Select(a => a.Name));

            _logger.LogError(
                "Bank import FAILED - Account not found: {AccountName}. Available accounts: {AvailableAccounts}",
                request.AccountName, availableAccounts);

            throw new ArgumentException(
                $"Account name {request.AccountName} not found in {BankAccountSettings.ConfigurationKey} configuration. Available accounts: {availableAccounts}");
        }
```

with:

```csharp
        var accountSetting = _bankSettings.Accounts.Single(a => a.Name == request.AccountName);
```

Nothing else in this file changes — the rest of `Handle`, `ProcessStatementAsync`, `InsertNewAsync`, and `UpsertExistingAsync` remain untouched.

- [ ] **Step 4: Run the full handler test suite to verify it still passes after the handler change**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportBankStatementHandlerTests"`
Expected: PASS — all tests green.

- [ ] **Step 5: Run the full Bank feature test suite (validator + handler + everything else in the module) to confirm no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank"`
Expected: PASS — all tests green, including `ImportBankStatementRequestValidatorTests` from the first task and `ImportBankStatementHandlerTests`.

- [ ] **Step 6: Check for any other test asserting the old 500/ArgumentException behavior at the controller or integration level**

Run: `grep -n "ArgumentException\|UNKNOWN" backend/test/Anela.Heblo.Tests/Controllers/BankStatementsControllerTests.cs backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs`

If this returns any match referencing an unknown-account/`ArgumentException` scenario for the import endpoint, update that test's expectation to match the new behavior (an unknown account now causes `FluentValidation.ValidationException` to be thrown before the handler runs, which `ValidationExceptionHandler` maps to HTTP 400 `ProblemDetails` — not a 500 or a raw `ArgumentException`). If it returns no match, no further action is needed for this step.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs
git commit -m "fix(bank): remove redundant ArgumentException now that validation guards ImportBankStatementRequest"
```

---

### task: full-verification

**Files:** none (verification-only task; no new files, no code changes)

- [ ] **Step 1: Full backend build**

Run: `dotnet build Anela.Heblo.sln` (repo root — this is the only solution file in the repo)
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Format check**

Run: `dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: No formatting changes required. If it reports changes, run `dotnet format Anela.Heblo.sln` (without `--verify-no-changes`) to apply them, then re-stage and amend the relevant commit from whichever of the three prior tasks introduced the unformatted code.

- [ ] **Step 3: Full backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: All tests pass, including the new `ImportBankStatementRequestValidatorTests` and the modified `ImportBankStatementHandlerTests`.

- [ ] **Step 4: Manual sanity check of the new behavior (optional, no server changes needed beyond what's already built)**

This step is descriptive only — there is no new script to run. If a local API instance is available, verify:
- `POST /api/bank-statements/import` with `{"accountName": "DOES-NOT-EXIST", "dateFrom": "2024-01-01", "dateTo": "2024-01-31"}` now returns HTTP 400 with a `ProblemDetails` body containing `errors: [{ propertyName: "AccountName", ... }]`, not HTTP 500.
- `POST /api/bank-statements/import` with `dateFrom` later than `dateTo` for a known account now returns HTTP 400 with `errors: [{ propertyName: "DateFrom", errorMessage: "DateFrom must not be later than DateTo" }]`.
- `POST /api/bank-statements/import` with a known account and a valid date range still behaves exactly as before (HTTP 200 with `BankStatementImportResultDto`).

No commit for this task — it only verifies the work already committed in the three prior tasks.
