using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderSchedule;
using Anela.Heblo.Application.Features.Manufacture.UseCases.DuplicateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;
using Anela.Heblo.Application.Features.Manufacture.UseCases.ResolveManualAction;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Manufacture_ManufactureOrders)]
[ApiController]
[Route("api/[controller]")]
public class ManufactureOrderController : BaseApiController
{
    private readonly IMediator _mediator;

    public ManufactureOrderController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all manufacture orders with optional filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<GetManufactureOrdersResponse>> GetOrders([FromQuery] GetManufactureOrdersRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// Get manufacture order by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<GetManufactureOrderResponse>> GetOrder(int id)
    {
        var request = new GetManufactureOrderRequest { Id = id };
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// Create manufacture order from batch calculation
    /// </summary>
    [HttpPost]
    [FeatureAuthorize(Feature.Manufacture_ManufactureOrders, AccessLevel.Write)]
    public async Task<ActionResult<CreateManufactureOrderResponse>> CreateOrder([FromBody] CreateManufactureOrderRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// Update manufacture order
    /// </summary>
    [HttpPut("{id}")]
    [FeatureAuthorize(Feature.Manufacture_ManufactureOrders, AccessLevel.Write)]
    public async Task<ActionResult<UpdateManufactureOrderResponse>> UpdateOrder(int id, [FromBody] UpdateManufactureOrderRequest request)
    {
        if (id != request.Id)
        {
            return BadRequest("ID in URL does not match ID in request body.");
        }

        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// Update manufacture order status
    /// </summary>
    [HttpPatch("{id}/status")]
    [FeatureAuthorize(Feature.Manufacture_ManufactureOrders, AccessLevel.Write)]
    public async Task<ActionResult<UpdateManufactureOrderStatusResponse>> UpdateOrderStatus(int id, [FromBody] UpdateManufactureOrderStatusRequest request)
    {
        if (id != request.Id)
        {
            return BadRequest("ID in URL does not match ID in request body.");
        }

        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// Confirm semi-product manufacture with actual quantity and change state from Planned to SemiProductManufactured
    /// </summary>
    [HttpPost("{id}/confirm-semi-product")]
    [FeatureAuthorize(Feature.Manufacture_ManufactureOrders, AccessLevel.Write)]
    public async Task<ActionResult<ConfirmSemiProductManufactureResponse>> ConfirmSemiProductManufacture(int id, [FromBody] ConfirmSemiProductManufactureRequest request)
    {
        if (id != request.Id)
        {
            return BadRequest("ID in URL does not match ID in request body.");
        }

        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// Confirm product completion with actual quantities and change state from SemiProductManufactured to Completed
    /// </summary>
    [HttpPost("{id}/confirm-products")]
    [FeatureAuthorize(Feature.Manufacture_ManufactureOrders, AccessLevel.Write)]
    public async Task<ActionResult<ConfirmProductCompletionResponse>> ConfirmProductCompletion(int id, [FromBody] ConfirmProductCompletionRequest request)
    {
        if (id != request.Id)
        {
            return BadRequest("ID in URL does not match ID in request body.");
        }

        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// Get calendar view of manufacture orders
    /// </summary>
    [HttpGet("calendar")]
    public async Task<ActionResult<GetCalendarViewResponse>> GetCalendarView([FromQuery] GetCalendarViewRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// Duplicate existing manufacture order with updated dates
    /// </summary>
    [HttpPost("{id}/duplicate")]
    [FeatureAuthorize(Feature.Manufacture_ManufactureOrders, AccessLevel.Write)]
    public async Task<ActionResult<DuplicateManufactureOrderResponse>> DuplicateOrder(int id)
    {
        var request = new DuplicateManufactureOrderRequest { SourceOrderId = id };
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// Resolve manual action for a manufacture order
    /// </summary>
    [HttpPost("{id}/resolve-manual-action")]
    [FeatureAuthorize(Feature.Manufacture_ManufactureOrders, AccessLevel.Write)]
    public async Task<ActionResult<ResolveManualActionResponse>> ResolveManualAction(int id, [FromBody] ResolveManualActionRequest request)
    {
        if (id != request.OrderId)
        {
            return BadRequest("ID in URL does not match ID in request body.");
        }

        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// Generate manufacture protocol PDF for a completed order
    /// </summary>
    [HttpGet("{id}/protocol.pdf")]
    public async Task<IActionResult> GetProtocolPdf(int id, CancellationToken cancellationToken)
    {
        var request = new GetManufactureProtocolRequest { Id = id };
        try
        {
            var response = await _mediator.Send(request, cancellationToken);
            return File(response.PdfBytes, "application/pdf", response.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update schedule dates for a manufacture order (used by drag & drop functionality)
    /// </summary>
    [HttpPatch("{id}/schedule")]
    [FeatureAuthorize(Feature.Manufacture_ManufactureOrders, AccessLevel.Write)]
    public async Task<ActionResult<UpdateManufactureOrderScheduleResponse>> UpdateOrderSchedule(int id, [FromBody] UpdateManufactureOrderScheduleRequest request)
    {
        if (id != request.Id)
        {
            return BadRequest("ID in URL does not match ID in request body.");
        }

        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }
}