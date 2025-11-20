using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeletePackingMaterial;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialLogs;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.ProcessDailyConsumption;
using Anela.Heblo.API.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/packing-materials")]
public class PackingMaterialsController : BaseApiController
{
    private readonly IMediator _mediator;

    public PackingMaterialsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetPackingMaterialsListResponse>> GetPackingMaterials(
        CancellationToken cancellationToken = default)
    {
        var request = new GetPackingMaterialsListRequest();
        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<CreatePackingMaterialResponse>> CreatePackingMaterial(
        [FromBody] CreatePackingMaterialRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var response = await _mediator.Send(request, cancellationToken);
        return CreatedAtAction(
            nameof(GetPackingMaterials),
            new { id = response.Id },
            response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<UpdatePackingMaterialResponse>> UpdatePackingMaterial(
        int id,
        [FromBody] UpdatePackingMaterialRequest request,
        CancellationToken cancellationToken = default)
    {
        if (id != request.Id)
        {
            return BadRequest("ID mismatch");
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("{id}/quantity")]
    public async Task<ActionResult<UpdatePackingMaterialQuantityResponse>> UpdatePackingMaterialQuantity(
        int id,
        [FromBody] UpdateQuantityRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var quantityRequest = new UpdatePackingMaterialQuantityRequest
        {
            Id = id,
            NewQuantity = request.NewQuantity,
            Date = request.Date
        };

        var response = await _mediator.Send(quantityRequest, cancellationToken);
        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeletePackingMaterial(
        int id,
        CancellationToken cancellationToken = default)
    {
        var request = new DeletePackingMaterialRequest { Id = id };
        await _mediator.Send(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("process-daily-consumption")]
    public async Task<ActionResult<ProcessDailyConsumptionResponse>> ProcessDailyConsumption(
        [FromBody] ProcessDailyConsumptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }

    [HttpGet("{id}/logs")]
    public async Task<ActionResult<GetPackingMaterialLogsResponse>> GetPackingMaterialLogs(
        int id,
        [FromQuery] int days = 60,
        CancellationToken cancellationToken = default)
    {
        var request = new GetPackingMaterialLogsRequest
        {
            PackingMaterialId = id,
            Days = days
        };

        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }
}