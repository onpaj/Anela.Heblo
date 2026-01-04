using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

/// <summary>
/// Represents margin data for a specific margin level (M0, M1, or M2)
/// </summary>
public class MarginLevelDto
{
    /// <summary>
    /// Margin percentage at this level
    /// </summary>
    [JsonPropertyName("percentage")]
    public decimal Percentage { get; set; }

    /// <summary>
    /// Absolute margin amount at this level (in currency)
    /// </summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    /// <summary>
    /// Cost specific to this level only (incremental cost)
    /// </summary>
    [JsonPropertyName("costLevel")]
    public decimal CostLevel { get; set; }

    /// <summary>
    /// Cumulative cost up to and including this level
    /// </summary>
    [JsonPropertyName("costTotal")]
    public decimal CostTotal { get; set; }
}
