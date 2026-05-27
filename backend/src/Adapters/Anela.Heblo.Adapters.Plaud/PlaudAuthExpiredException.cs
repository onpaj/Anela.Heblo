namespace Anela.Heblo.Adapters.Plaud;

public sealed class PlaudAuthExpiredException : Exception
{
    public PlaudAuthExpiredException(string stderr)
        : base($"Plaud authentication expired. Run `plaud login` and update App Service setting `Plaud__TokensJson`. CLI stderr: {stderr ?? "(empty)"}")
    { }

    public PlaudAuthExpiredException(string stderr, Exception innerException)
        : base($"Plaud authentication expired. Run `plaud login` and update App Service setting `Plaud__TokensJson`. CLI stderr: {stderr ?? "(empty)"}", innerException)
    { }
}
