namespace Anela.Heblo.Domain.Features.Configuration;

/// <summary>
/// Configuration options for product export functionality
/// </summary>
public class ProductExportOptions
{
    /// <summary>
    /// The URL from which product export files will be downloaded
    /// </summary>
    public string Url { get; set; } = null!;

    public string ContainerName { get; set; }

    /// <summary>
    /// Timeout for the HTTP HEAD probe used to verify export availability.
    /// </summary>
    public TimeSpan HeadTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Timeout for the full export file download.
    /// </summary>
    public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Maximum number of retry attempts for transient HTTP failures.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay for the exponential back-off retry policy.
    /// </summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
}
