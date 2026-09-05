namespace Anela.Heblo.Domain.Features.ProductPricing;

public enum PriceSyncStatus
{
    InSync = 0,
    Pending = 1,
    Conflict = 2,
    Failed = 3,
}
