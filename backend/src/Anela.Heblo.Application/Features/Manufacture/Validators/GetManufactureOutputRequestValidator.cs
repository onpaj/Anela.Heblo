using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOutput;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Manufacture.Validators;

public class GetManufactureOutputRequestValidator : AbstractValidator<GetManufactureOutputRequest>
{
    public GetManufactureOutputRequestValidator()
    {
        RuleFor(x => x.MonthsBack)
            .GreaterThanOrEqualTo(ManufactureConstants.MIN_MONTHS_BACK)
            .WithMessage($"MonthsBack must be at least {ManufactureConstants.MIN_MONTHS_BACK}")
            .LessThanOrEqualTo(ManufactureConstants.MAX_MONTHS_BACK)
            .WithMessage($"MonthsBack cannot exceed {ManufactureConstants.MAX_MONTHS_BACK}");
    }
}