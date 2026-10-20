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
