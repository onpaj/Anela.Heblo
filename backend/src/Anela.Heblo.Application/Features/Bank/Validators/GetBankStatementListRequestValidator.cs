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