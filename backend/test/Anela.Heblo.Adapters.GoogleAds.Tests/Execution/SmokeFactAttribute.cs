namespace Anela.Heblo.Adapters.GoogleAds.Tests.Execution;

/// <summary>A Fact that runs only when HEBLO_GOOGLE_ADS_SMOKE_ENV points at a google-ads.env file.</summary>
public sealed class SmokeFactAttribute : FactAttribute
{
    public const string EnvFileVariable = "HEBLO_GOOGLE_ADS_SMOKE_ENV";

    public SmokeFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvFileVariable)))
            Skip = $"Manual smoke test: set {EnvFileVariable} (see docs/integrations/google-ads-api.md §12).";
    }
}
