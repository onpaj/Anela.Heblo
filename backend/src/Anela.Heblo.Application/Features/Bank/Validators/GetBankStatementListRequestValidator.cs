using FluentValidation;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

namespace Anela.Heblo.Application.Features.Bank.Validators;

public class GetBankStatementListRequestValidator : AbstractValidator<GetBankStatementListRequest>
{
    public GetBankStatementListRequestValidator()
    {
        RuleFor(x => x.Take)
            .GreaterThan(0)
            .WithMessage("Take must be greater than 0")
            .LessThanOrEqualTo(100)
            .WithMessage("Take must not exceed 100");

        RuleFor(x => x.Skip)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Skip must be greater than or equal to 0");
    }
}