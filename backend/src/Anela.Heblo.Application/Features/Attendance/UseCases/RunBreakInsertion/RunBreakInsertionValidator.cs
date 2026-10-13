using FluentValidation;

namespace Anela.Heblo.Application.Features.Attendance.UseCases.RunBreakInsertion;

public class RunBreakInsertionValidator : AbstractValidator<RunBreakInsertionRequest>
{
    /// <summary>
    /// The walk runs synchronously so the caller gets the summary back, and every day in the window
    /// can cost several sequential Logeto round trips. This caps one call well inside the request
    /// timeout; a longer sweep is several calls, each moving the window further back.
    /// </summary>
    public const int MaxWindowDays = 14;

    public RunBreakInsertionValidator()
    {
        RuleFor(x => x.FromDaysAgo)
            .GreaterThanOrEqualTo(0)
            .When(x => x.FromDaysAgo.HasValue)
            .WithMessage("Počátek okna nesmí být záporný.");

        RuleFor(x => x.ToDaysAgo)
            .GreaterThanOrEqualTo(0)
            .When(x => x.ToDaysAgo.HasValue)
            .WithMessage("Konec okna nesmí být záporný.");

        // Only checked when the caller picks the start themselves; otherwise the window comes from
        // the configured nightly lookback, which is not the caller's to get wrong.
        RuleFor(x => x)
            .Must(x => WindowDays(x) >= 0)
            .When(x => x.FromDaysAgo is >= 0)
            .WithMessage("Počátek okna musí být starší nebo stejný jako jeho konec.");

        RuleFor(x => x)
            .Must(x => WindowDays(x) < MaxWindowDays)
            .When(x => x.FromDaysAgo is >= 0)
            .WithMessage($"Okno smí pokrývat nejvýše {MaxWindowDays} dní. Rozdělte průchod na několik volání.");
    }

    /// <summary>Days between the window's start and end, with the end defaulting to today.</summary>
    private static int WindowDays(RunBreakInsertionRequest request) =>
        request.FromDaysAgo!.Value - (request.ToDaysAgo ?? 0);
}
