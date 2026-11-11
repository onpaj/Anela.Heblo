namespace Anela.Heblo.Application.Features.Logistics.Contracts.Models;

/// <summary>
/// Logistics-owned view of one component part of a gift package set.
/// </summary>
public class LogisticsGiftPackagePart
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public double Amount { get; set; }
}
