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
}