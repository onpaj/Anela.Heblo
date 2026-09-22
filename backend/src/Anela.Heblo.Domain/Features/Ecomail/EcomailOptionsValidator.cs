using Microsoft.Extensions.Options;

namespace Anela.Heblo.Domain.Features.Ecomail;

public class EcomailOptionsValidator : IValidateOptions<EcomailOptions>
{
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

        // ApiKey is deliberately not required: an empty key disables the module rather than
        // breaking startup for every developer who has no Ecomail credentials.
        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
