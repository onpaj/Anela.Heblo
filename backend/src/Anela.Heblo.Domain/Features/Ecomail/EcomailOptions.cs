namespace Anela.Heblo.Domain.Features.Ecomail;

public class EcomailOptions
{
    public const string SectionName = "Ecomail";

    /// <summary>From Key Vault secret `Ecomail--ApiKey`. Empty disables the whole module.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api2.ecomailapp.cz";

    public int HttpTimeoutSeconds { get; set; } = 60;

    /// <summary>Every 6 hours — the full pull is ~400 calls and "current newsletter" numbers go stale overnight.</summary>
    public string CronExpression { get; set; } = "0 */6 * * *";

    public string TimeZone { get; set; } = "Europe/Prague";

    /// <summary>How many months of automation event windows the job recomputes. 2 = current + previous.</summary>
    public int RecomputeWindowMonths { get; set; } = 2;

    /// <summary>First month of automation history to backfill. The account's oldest send is 2024-11.</summary>
    public DateOnly BackfillFrom { get; set; } = new(2024, 11, 1);
}
