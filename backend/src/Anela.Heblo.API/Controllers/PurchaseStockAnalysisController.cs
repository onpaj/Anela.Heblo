using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Purchase_PurchaseStock)]
[ApiController]
[Route("api/purchase-stock-analysis")]
public class PurchaseStockAnalysisController : BaseApiController
{
    private readonly IMediator _mediator;

    public PurchaseStockAnalysisController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetPurchaseStockAnalysisResponse>> GetStockAnalysis(
        [FromQuery] GetPurchaseStockAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }
}