namespace Anela.Heblo.Application.Features.Configuration.Domain;

/// <summary>
/// Domain model representing application configuration
/// </summary>
public class ApplicationConfiguration
{
    public string Version { get; private set; }
    public string Environment { get; private set; }
    public bool UseMockAuth { get; private set; }
    public DateTime Timestamp { get; private set; }

    public ApplicationConfiguration(string version, string environment, bool useMockAuth)
    {
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        UseMockAuth = useMockAuth;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates configuration with fallback values
    /// </summary>
    public static ApplicationConfiguration CreateWithDefaults(string? version, string? environment, bool useMockAuth)
    {
        return new ApplicationConfiguration(
            version ?? "1.0.0",
            environment ?? "Production",
            useMockAuth
        );
    }
}