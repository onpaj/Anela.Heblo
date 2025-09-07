using Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/manufacturing-stock-analysis")]
public class ManufacturingStockAnalysisController : BaseApiController
{
    private readonly IMediator _mediator;

    public ManufacturingStockAnalysisController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetManufacturingStockAnalysisResponse>> GetStockAnalysis(
        [FromQuery] GetManufacturingStockAnalysisRequest request,
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