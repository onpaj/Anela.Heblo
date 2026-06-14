namespace Anela.Heblo.Adapters.Plaud;

public sealed class PlaudAuthExpiredException : Exception
{
    private const string Guidance =
        "Plaud authentication expired. Refresh failed or refresh token is dead. " +
        "Run `plaud login` locally and rotate the Plaud--TokensJson secret in Key Vault. " +
        "See docs/operations/plaud-token-rotation.md.";

    public PlaudAuthExpiredException(string stderr)
        : base($"{Guidance} CLI stderr: {stderr ?? "(empty)"}")
    { }

    public PlaudAuthExpiredException(string stderr, Exception innerException)
        : base($"{Guidance} CLI stderr: {stderr ?? "(empty)"}", innerException)
    { }
}
