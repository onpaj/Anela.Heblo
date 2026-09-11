using Anela.Heblo.API.Infrastructure;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.GetPriceDivergenceReport;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.SyncProductPrices;
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

    /// <summary>Read-only comparison of Shoptet and Flexi prices. Never writes anywhere.</summary>
    [HttpGet("divergence")]
    public async Task<ActionResult<GetPriceDivergenceReportResponse>> GetDivergenceReport(CancellationToken cancellationToken = default)
        => HandleResponse(await _mediator.Send(new GetPriceDivergenceReportRequest(), cancellationToken));

    /// <summary>
    /// Re-reads Shoptet and Flexi for the named products and returns their fresh comparison
    /// rows. A read like the divergence report — it writes nowhere — so it stays on the read
    /// permission; POST only because the selection belongs in a body, not a query string.
    /// </summary>
    [HttpPost("sync")]
    public async Task<ActionResult<SyncProductPricesResponse>> Sync(
        [FromBody] SyncProductPricesRequest request,
        CancellationToken cancellationToken = default)
        => HandleResponse(await _mediator.Send(request, cancellationToken));

    [HttpPut("prices/{productCode}")]
    [FeatureAuthorize(Feature.Products_Catalog, AccessLevel.Write)]
    public async Task<ActionResult<SetProductPriceResponse>> SetPrice(
        string productCode,
        [FromBody] SetProductPriceRequest request,
        CancellationToken cancellationToken = default)
    {
        request.ProductCode = productCode;
        return HandleResponse(await _mediator.Send(request, cancellationToken));
    }
}
