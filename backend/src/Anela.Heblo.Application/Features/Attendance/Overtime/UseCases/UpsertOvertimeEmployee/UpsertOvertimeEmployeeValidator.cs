using FluentValidation;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.UpsertOvertimeEmployee;

public class UpsertOvertimeEmployeeValidator : AbstractValidator<UpsertOvertimeEmployeeRequest>
{
    public UpsertOvertimeEmployeeValidator()
    {
        RuleFor(x => x.PersonId).NotEmpty();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BaselineHours).InclusiveBetween(-1000m, 1000m);
        RuleFor(x => x.BaselineDate).NotEmpty();
    }
}
