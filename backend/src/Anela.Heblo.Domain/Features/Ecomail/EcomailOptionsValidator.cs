using Microsoft.Extensions.Options;

namespace Anela.Heblo.Domain.Features.Ecomail;

public class EcomailOptionsValidator : IValidateOptions<EcomailOptions>
{
    /// <summary>Ecomail itself launched well after this; anything earlier is a misconfiguration.</summary>
    private static readonly DateOnly EarliestBackfillFrom = new(2020, 1, 1);

    public ValidateOptionsResult Validate(string? name, EcomailOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BaseUrl) ||
            !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            errors.Add("Ecomail:BaseUrl must be an absolute URL.");
        }

        if (options.HttpTimeoutSeconds is < 1 or > 600)
        {
            errors.Add("Ecomail:HttpTimeoutSeconds must be between 1 and 600.");
        }

        if (options.RecomputeWindowMonths < 1)
        {
            errors.Add("Ecomail:RecomputeWindowMonths must be >= 1.");
        }

        // A BackfillFrom in the future leaves the month loop with nothing to iterate, so the job
        // reports a clean run and never computes a single month. An absurdly early one turns the
        // first run into thousands of sequential calls. Neither is recoverable by the job itself.
        var currentMonth = DateOnly.FromDateTime(DateTime.UtcNow);
        if (options.BackfillFrom > currentMonth)
        {
            errors.Add("Ecomail:BackfillFrom must not be in the future.");
        }
        else if (options.BackfillFrom < EarliestBackfillFrom)
        {
            errors.Add($"Ecomail:BackfillFrom must not be earlier than {EarliestBackfillFrom:yyyy-MM-dd}.");
        }

        // ApiKey is deliberately not required: an empty key disables the module rather than
        // breaking startup for every developer who has no Ecomail credentials.
        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
