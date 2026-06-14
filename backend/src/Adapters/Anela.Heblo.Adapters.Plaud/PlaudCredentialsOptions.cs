namespace Anela.Heblo.Adapters.Plaud;

public sealed class PlaudCredentialsOptions
{
    public const string SectionKey = "Plaud:Credentials";

    public string TokensJsonSecretName { get; init; } = "Plaud--TokensJson";
    public TimeSpan ExpiryBuffer { get; init; } = TimeSpan.FromHours(72);
    public TimeSpan RefreshTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
