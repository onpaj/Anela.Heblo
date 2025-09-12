using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/manufacture-batch")]
public class ManufactureBatchController : BaseApiController
{
    private readonly IMediator _mediator;

    public ManufactureBatchController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("template/{productCode}")]
    public async Task<ActionResult<CalculatedBatchSizeResponse>> GetBatchTemplate(
        string productCode,
        CancellationToken cancellationToken = default)
    {
        var request = new CalculatedBatchSizeRequest { ProductCode = productCode };
        var response = await _mediator.Send(request, cancellationToken);

        return HandleResponse(response);
    }

    [HttpPost("calculate-by-size")]
    public async Task<ActionResult<CalculatedBatchSizeResponse>> CalculateBatchBySize(
        [FromBody] CalculatedBatchSizeRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);

        return HandleResponse(response);
    }

    [HttpPost("calculate-by-ingredient")]
    public async Task<ActionResult<CalculateBatchByIngredientResponse>> CalculateBatchByIngredient(
        [FromBody] CalculateBatchByIngredientRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);

        return HandleResponse(response);
    }

    [HttpPost("calculate-batch-plan")]
    public async Task<ActionResult<CalculateBatchPlanResponse>> CalculateBatchPlan(
        [FromBody] CalculateBatchPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);

        return HandleResponse(response);
    }
}