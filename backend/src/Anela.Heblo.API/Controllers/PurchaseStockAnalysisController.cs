using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
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