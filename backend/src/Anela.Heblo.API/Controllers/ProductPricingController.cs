using Anela.Heblo.API.Infrastructure;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.GetProductPrices;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.TriggerPriceSync;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Products_Catalog)]
[ApiController]
[Route("api/product-pricing")]
public class ProductPricingController : BaseApiController
{
    private readonly IMediator _mediator;

    public ProductPricingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("prices")]
    public async Task<ActionResult<GetProductPricesResponse>> GetPrices(CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetProductPricesRequest(), cancellationToken));

    [HttpPut("prices/{productCode}")]
    [FeatureAuthorize(Feature.Products_Catalog, AccessLevel.Write)]
    public async Task<ActionResult<SetProductPriceResponse>> SetPrice(
        string productCode,
        [FromBody] SetProductPriceRequest request,
        CancellationToken cancellationToken = default)
    {
        request.ProductCode = productCode;
        return Ok(await _mediator.Send(request, cancellationToken));
    }

    [HttpPost("sync")]
    [FeatureAuthorize(Feature.Products_Catalog, AccessLevel.Write)]
    public async Task<ActionResult<TriggerPriceSyncResponse>> TriggerSync(CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new TriggerPriceSyncRequest(), cancellationToken));
}
