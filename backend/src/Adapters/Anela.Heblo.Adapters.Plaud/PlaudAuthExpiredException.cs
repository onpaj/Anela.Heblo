namespace Anela.Heblo.Adapters.Plaud;

public sealed class PlaudAuthExpiredException : Exception
{
    // Recovery must target the Key Vault secret, NOT the App Service env var: Key Vault overrides
    // the `Plaud__TokensJson` env var at startup, so editing the env var has no effect.
    private const string RecoveryHint =
        "Plaud authentication expired (stored refresh token is dead). Recover: run `plaud login`, "
        + "then write the fresh ~/.plaud/tokens.json into Key Vault secret `Plaud--TokensJson` "
        + "(kv-heblo-prod / kv-heblo-stg) and restart the app. "
        + "See docs/integrations/plaud-token-auto-refresh.md.";

    public PlaudAuthExpiredException(string stderr)
        : base($"{RecoveryHint} CLI stderr: {stderr ?? "(empty)"}")
    { }

    public PlaudAuthExpiredException(string stderr, Exception innerException)
        : base($"{RecoveryHint} CLI stderr: {stderr ?? "(empty)"}", innerException)
    { }
}
