using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.TriggerPriceSync;

public class TriggerPriceSyncResponse : BaseResponse
{
    public int Pushed { get; set; }
    public int Conflicts { get; set; }
    public int Failed { get; set; }
    public int Seeded { get; set; }
    public int Unchanged { get; set; }
}
