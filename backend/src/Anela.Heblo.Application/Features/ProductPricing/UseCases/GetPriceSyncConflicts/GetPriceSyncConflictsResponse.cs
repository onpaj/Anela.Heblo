using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.GetPriceSyncConflicts;

public class GetPriceSyncConflictsResponse : BaseResponse
{
    public List<PriceSyncConflictDto> Conflicts { get; set; } = new();
}
