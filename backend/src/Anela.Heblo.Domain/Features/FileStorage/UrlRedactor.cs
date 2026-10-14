namespace Anela.Heblo.Domain.Features.FileStorage;

public static class UrlRedactor
{
    private const string Redacted = "[redacted]";

    /// <summary>
    /// Strips query, fragment and user-info from a URL so it is safe to log.
    /// Returns "[redacted]" for null, empty or unparseable input. Never throws.
    /// </summary>
    public static string Redact(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Redacted;
        }

        try
        {
            return new UriBuilder(url)
            {
                Query = string.Empty,
                Fragment = string.Empty,
                UserName = string.Empty,
                Password = string.Empty
            }.Uri.ToString();
        }
        catch (Exception)
        {
            return Redacted;
        }
    }
}
