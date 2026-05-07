using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreateAllocation;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeleteAllocation;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeletePackingMaterial;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetAllocations;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetDailyConsumptionBreakdown;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialLogs;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.ProcessDailyConsumption;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdateAllocation;
using Anela.Heblo.API.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

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

    [HttpGet("consumption")]
    public async Task<ActionResult<GetDailyConsumptionBreakdownResponse>> GetDailyConsumptionBreakdown(
        [FromQuery] string date,
        [FromQuery] string groupBy = "material",
        CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            return BadRequest(new { error = "Invalid date format. Expected yyyy-MM-dd." });
        }

        var request = new GetDailyConsumptionBreakdownRequest { Date = parsedDate, GroupBy = groupBy };
        var response = await _mediator.Send(request, cancellationToken);
        return response.Success ? Ok(response) : BadRequest(new { error = response.Error });
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

    [HttpGet("{id}/allocations")]
    public async Task<ActionResult<GetAllocationsResponse>> GetAllocations(
        int id,
        CancellationToken cancellationToken = default)
    {
        var request = new GetAllocationsRequest { PackingMaterialId = id };
        var response = await _mediator.Send(request, cancellationToken);
        return response.Success ? Ok(response) : BadRequest(new { error = response.Error });
    }

    [HttpPost("{id}/allocations")]
    public async Task<ActionResult<CreateAllocationResponse>> CreateAllocation(
        int id,
        [FromBody] CreateAllocationRequestBody body,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateAllocationRequest
        {
            PackingMaterialId = id,
            ProductCode = body.ProductCode,
            AmountPerUnit = body.AmountPerUnit
        };

        var response = await _mediator.Send(request, cancellationToken);
        if (!response.Success)
        {
            return BadRequest(new { error = response.Error });
        }

        return CreatedAtAction(nameof(GetAllocations), new { id }, response);
    }

    [HttpPut("{id}/allocations/{allocationId}")]
    public async Task<ActionResult<UpdateAllocationResponse>> UpdateAllocation(
        int id,
        int allocationId,
        [FromBody] UpdateAllocationRequestBody body,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateAllocationRequest
        {
            PackingMaterialId = id,
            AllocationId = allocationId,
            ProductCode = body.ProductCode,
            AmountPerUnit = body.AmountPerUnit
        };

        var response = await _mediator.Send(request, cancellationToken);
        return response.Success ? Ok(response) : BadRequest(new { error = response.Error });
    }

    [HttpDelete("{id}/allocations/{allocationId}")]
    public async Task<ActionResult<DeleteAllocationResponse>> DeleteAllocation(
        int id,
        int allocationId,
        CancellationToken cancellationToken = default)
    {
        var request = new DeleteAllocationRequest { PackingMaterialId = id, AllocationId = allocationId };
        var response = await _mediator.Send(request, cancellationToken);
        return response.Success ? NoContent() : BadRequest(new { error = response.Error });
    }
}

public class CreateAllocationRequestBody
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal AmountPerUnit { get; set; }
}

public class UpdateAllocationRequestBody
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal AmountPerUnit { get; set; }
}