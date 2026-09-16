using FluentValidation;

namespace Anela.Heblo.Application.Features.Attendance.UseCases.RunBreakInsertion;

public class RunBreakInsertionValidator : AbstractValidator<RunBreakInsertionRequest>
{
    /// <summary>
    /// The walk runs synchronously so the caller gets the summary back. This cap keeps one call
    /// comfortably inside the request timeout — a longer sweep is several successive calls.
    /// </summary>
    public const int MaxLookbackDays = 31;

    public RunBreakInsertionValidator()
    {
        RuleFor(x => x.LookbackDays)
            .InclusiveBetween(0, MaxLookbackDays)
            .When(x => x.LookbackDays.HasValue);
    }
}
