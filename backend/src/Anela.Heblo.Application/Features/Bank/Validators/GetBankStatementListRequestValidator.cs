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