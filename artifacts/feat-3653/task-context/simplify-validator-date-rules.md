## Goal (from the overall plan)

Retype the four date filter fields on `GetBankStatementListRequest` (and the matching `BankStatementsController` query parameters) from `string?` to `DateTime?`, deleting the now-redundant `DateTime.TryParse` logic duplicated across the handler and validator, and adapting the frontend client/hook to match.

This task is task 3 of 5. Tasks 1 (`retype-request-and-controller-date-fields`) and 2 (`remove-handler-date-parsing`) are already done on this branch — the DTO/controller are retyped to `DateTime?` and the handler's manual parsing is removed. This task finishes the backend retype by simplifying the validator, which is the last piece blocking compilation.

---

### task: simplify-validator-date-rules

**File:** `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs`

**Step 1 — rewrite the validator.**

Current file:

```csharp
using FluentValidation;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

namespace Anela.Heblo.Application.Features.Bank.Validators;

public class GetBankStatementListRequestValidator : AbstractValidator<GetBankStatementListRequest>
{
    private const int MaxStringLength = 100;

    public GetBankStatementListRequestValidator()
    {
        RuleFor(x => x.Take)
            .GreaterThan(0).WithMessage("Take must be greater than 0")
            .LessThanOrEqualTo(100).WithMessage("Take must not exceed 100");

        RuleFor(x => x.Skip)
            .GreaterThanOrEqualTo(0).WithMessage("Skip must be greater than or equal to 0");

        RuleFor(x => x.TransferId!)
            .MaximumLength(MaxStringLength)
            .WithMessage($"TransferId must not exceed {MaxStringLength} characters")
            .When(x => x.TransferId != null);

        RuleFor(x => x.Account!)
            .MaximumLength(MaxStringLength)
            .WithMessage($"Account must not exceed {MaxStringLength} characters")
            .When(x => x.Account != null);

        RuleFor(x => x.DateFrom!)
            .Must(BeParseableDate)
            .WithMessage("DateFrom must be a valid date")
            .When(x => !string.IsNullOrWhiteSpace(x.DateFrom));

        RuleFor(x => x.DateTo!)
            .Must(BeParseableDate)
            .WithMessage("DateTo must be a valid date")
            .When(x => !string.IsNullOrWhiteSpace(x.DateTo));

        RuleFor(x => x.DateFrom!)
            .Must((req, _) => DateFromIsNotLaterThanDateTo(req))
            .WithMessage("DateFrom must not be later than DateTo")
            .When(x => BeParseableDate(x.DateFrom) && BeParseableDate(x.DateTo));
    }

    private static bool BeParseableDate(string? value) =>
        string.IsNullOrWhiteSpace(value) || DateTime.TryParse(value, out _);

    private static bool DateFromIsNotLaterThanDateTo(GetBankStatementListRequest req)
    {
        if (!DateTime.TryParse(req.DateFrom, out var from)) return true;
        if (!DateTime.TryParse(req.DateTo, out var to)) return true;
        return from.Date <= to.Date;
    }
}
```

Replace it entirely with:

```csharp
using FluentValidation;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

namespace Anela.Heblo.Application.Features.Bank.Validators;

public class GetBankStatementListRequestValidator : AbstractValidator<GetBankStatementListRequest>
{
    private const int MaxStringLength = 100;

    public GetBankStatementListRequestValidator()
    {
        RuleFor(x => x.Take)
            .GreaterThan(0).WithMessage("Take must be greater than 0")
            .LessThanOrEqualTo(100).WithMessage("Take must not exceed 100");

        RuleFor(x => x.Skip)
            .GreaterThanOrEqualTo(0).WithMessage("Skip must be greater than or equal to 0");

        RuleFor(x => x.TransferId!)
            .MaximumLength(MaxStringLength)
            .WithMessage($"TransferId must not exceed {MaxStringLength} characters")
            .When(x => x.TransferId != null);

        RuleFor(x => x.Account!)
            .MaximumLength(MaxStringLength)
            .WithMessage($"Account must not exceed {MaxStringLength} characters")
            .When(x => x.Account != null);

        RuleFor(x => x.DateFrom)
            .Must((req, dateFrom) => !dateFrom.HasValue || !req.DateTo.HasValue || dateFrom.Value.Date <= req.DateTo.Value.Date)
            .WithMessage("DateFrom must not be later than DateTo");
    }
}
```

Notes on this rewrite:
- `BeParseableDate` and both `.Must(BeParseableDate)` rules are gone — unparseable strings can no longer reach the validator, they are rejected earlier by ASP.NET Core model binding (task 1 of this plan).
- `DateFromIsNotLaterThanDateTo` as a named private method is gone; the comparison is now an inline lambda directly on the `RuleFor(x => x.DateFrom)` chain.
- No `.When(...)` guard is needed on this rule any more (the old guard existed only to skip the check when either date failed to parse as a string — that scenario can't occur now); the null-guard inside the `Must` lambda (`!dateFrom.HasValue || !req.DateTo.HasValue`) already makes the rule pass when either side is null, matching current `Validate_AcceptsAllNullOptionalFields`/`Validate_AcceptsValidDateRange` semantics.
- `TransferId`/`Account` rules are untouched.

**Step 2 — verify the backend now compiles.**

Run:

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: build succeeds (0 errors). This confirms `Application` and `API` projects compile with the new types end-to-end. The test project (`Anela.Heblo.Tests`) is **not** part of this build target and is still broken — that's expected, fixed in the `update-backend-unit-tests` task.

**Step 3 — commit.**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs
git commit -m "Simplify GetBankStatementListRequestValidator to compare typed DateTime values"
```
