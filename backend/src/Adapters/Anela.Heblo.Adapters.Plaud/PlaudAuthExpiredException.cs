namespace Anela.Heblo.Adapters.Plaud;

public sealed class PlaudAuthExpiredException : Exception
{
    public PlaudAuthExpiredException(string stderr)
        : base($"Plaud authentication expired. Run `plaud login` and update App Service setting `Plaud__TokensJson`. CLI stderr: {stderr}")
    { }
}
