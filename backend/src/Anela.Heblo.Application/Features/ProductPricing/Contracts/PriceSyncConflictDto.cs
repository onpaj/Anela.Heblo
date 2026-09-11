using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Application.Features.ProductPricing.Contracts;

public class PriceSyncConflictDto
{
    public string ProductCode { get; set; } = string.Empty;
    public PriceSyncTarget Target { get; set; }
    public decimal HebloPriceWithVat { get; set; }
    public decimal? RemotePriceWithVat { get; set; }
    public DateTime? ConflictDetectedAt { get; set; }
}
