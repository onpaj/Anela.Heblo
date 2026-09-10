using Anela.Heblo.API.Infrastructure;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.GetPriceDivergenceReport;
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
}
