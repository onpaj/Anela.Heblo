using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreateAllocation;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeleteAllocation;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeletePackingMaterial;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetAllocations;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetDailyConsumptionBreakdown;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialLogs;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.ProcessDailyConsumption;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdateAllocation;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterialQuantity;
using Anela.Heblo.API.Infrastructure;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Warehouse_Logistics)]
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
    [FeatureAuthorize(Feature.Warehouse_Logistics, AccessLevel.Write)]
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
    [FeatureAuthorize(Feature.Warehouse_Logistics, AccessLevel.Write)]
    [ProducesResponseType(typeof(UpdatePackingMaterialResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
        if (response.Success) return Ok(response);
        if (response.ErrorCode == ErrorCodes.ResourceNotFound) return NotFound(new { error = response.Error });
        return BadRequest(new { error = response.Error });
    }

    [HttpPost("{id}/quantity")]
    [FeatureAuthorize(Feature.Warehouse_Logistics, AccessLevel.Write)]
    [ProducesResponseType(typeof(UpdatePackingMaterialQuantityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
        if (response.Success) return Ok(response);
        if (response.ErrorCode == ErrorCodes.ResourceNotFound) return NotFound(new { error = response.Error });
        return BadRequest(new { error = response.Error });
    }

    [HttpDelete("{id}")]
    [FeatureAuthorize(Feature.Warehouse_Logistics, AccessLevel.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeletePackingMaterial(
        int id,
        CancellationToken cancellationToken = default)
    {
        var request = new DeletePackingMaterialRequest { Id = id };
        var response = await _mediator.Send(request, cancellationToken);
        if (response.Success) return NoContent();
        if (response.ErrorCode == ErrorCodes.ResourceNotFound) return NotFound(new { error = response.Error });
        return BadRequest(new { error = response.Error });
    }

    [HttpPost("process-daily-consumption")]
    [FeatureAuthorize(Feature.Warehouse_Logistics, AccessLevel.Write)]
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
    [ProducesResponseType(typeof(GetDailyConsumptionBreakdownResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetDailyConsumptionBreakdownResponse>> GetDailyConsumptionBreakdown(
        [FromQuery] string? date,
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

    [HttpGet("consumption-history")]
    [ProducesResponseType(typeof(GetConsumptionHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetConsumptionHistoryResponse>> GetConsumptionHistory(
        [FromQuery] GetConsumptionHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }

    [HttpGet("{id}/logs")]
    [ProducesResponseType(typeof(GetPackingMaterialLogsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
        if (response.Success) return Ok(response);
        if (response.ErrorCode == ErrorCodes.ResourceNotFound) return NotFound(new { error = response.Error });
        return BadRequest(new { error = response.Error });
    }

    [HttpGet("{id}/allocations")]
    [ProducesResponseType(typeof(GetAllocationsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetAllocationsResponse>> GetAllocations(
        int id,
        CancellationToken cancellationToken = default)
    {
        var request = new GetAllocationsRequest { PackingMaterialId = id };
        var response = await _mediator.Send(request, cancellationToken);
        if (response.Success) return Ok(response);
        if (response.ErrorCode == ErrorCodes.ResourceNotFound) return NotFound(new { error = response.Error });
        return BadRequest(new { error = response.Error });
    }

    [HttpPost("{id}/allocations")]
    [FeatureAuthorize(Feature.Warehouse_Logistics, AccessLevel.Write)]
    [ProducesResponseType(typeof(CreateAllocationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
            if (response.ErrorCode == ErrorCodes.ResourceNotFound) return NotFound(new { error = response.Error });
            return BadRequest(new { error = response.Error });
        }

        return CreatedAtAction(nameof(GetAllocations), new { id }, response);
    }

    [HttpPut("{id}/allocations/{allocationId}")]
    [FeatureAuthorize(Feature.Warehouse_Logistics, AccessLevel.Write)]
    [ProducesResponseType(typeof(UpdateAllocationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
        if (response.Success) return Ok(response);
        if (response.ErrorCode == ErrorCodes.ResourceNotFound) return NotFound(new { error = response.Error });
        return BadRequest(new { error = response.Error });
    }

    [HttpDelete("{id}/allocations/{allocationId}")]
    [FeatureAuthorize(Feature.Warehouse_Logistics, AccessLevel.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeleteAllocationResponse>> DeleteAllocation(
        int id,
        int allocationId,
        CancellationToken cancellationToken = default)
    {
        var request = new DeleteAllocationRequest { PackingMaterialId = id, AllocationId = allocationId };
        var response = await _mediator.Send(request, cancellationToken);
        if (response.Success) return NoContent();
        if (response.ErrorCode == ErrorCodes.ResourceNotFound) return NotFound(new { error = response.Error });
        return BadRequest(new { error = response.Error });
    }
}
