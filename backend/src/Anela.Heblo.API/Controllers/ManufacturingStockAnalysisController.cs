using Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Manufacture_ManufactureStock)]
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