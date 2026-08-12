using FluentValidation;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CreateAdjustment;

public class CreateAdjustmentValidator : AbstractValidator<CreateAdjustmentRequest>
{
    public CreateAdjustmentValidator()
    {
        RuleFor(x => x.PersonId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween(2020, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Hours).InclusiveBetween(-1000m, 1000m);
        RuleFor(x => x.Note).NotEmpty().MaximumLength(500);
    }
}
