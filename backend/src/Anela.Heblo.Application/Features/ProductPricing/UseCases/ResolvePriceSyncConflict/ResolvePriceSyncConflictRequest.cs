using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Domain.Features.ProductPricing;
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.ResolvePriceSyncConflict;

public class ResolvePriceSyncConflictRequest : IRequest<ResolvePriceSyncConflictResponse>
{
    public string ProductCode { get; set; } = string.Empty;
    public PriceSyncTarget Target { get; set; }
    public PriceConflictResolution Resolution { get; set; }
}
