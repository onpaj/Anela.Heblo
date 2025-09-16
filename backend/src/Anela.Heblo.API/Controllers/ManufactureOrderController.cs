using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ManufactureOrderController : BaseApiController
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    public ManufactureOrderController(IMediator mediator, IConfiguration configuration)
    {
        _mediator = mediator;
        _configuration = configuration;
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
    public async Task<ActionResult<CreateManufactureOrderResponse>> CreateOrder([FromBody] CreateManufactureOrderRequest request)
    {
        var response = await _mediator.Send(request);

        if (response.Success)
        {
            return Created($"/api/ManufactureOrder/{response.Id}", response);
        }

        return HandleResponse(response);
    }

    /// <summary>
    /// Update manufacture order
    /// </summary>
    [HttpPut("{id}")]
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
    /// Get calendar view of manufacture orders
    /// </summary>
    [HttpGet("calendar")]
    public async Task<ActionResult<GetCalendarViewResponse>> GetCalendarView([FromQuery] GetCalendarViewRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// Get responsible persons from Entra ID group for manufacture orders
    /// </summary>
    [HttpGet("responsible-persons")]
    public async Task<ActionResult<GetGroupMembersResponse>> GetResponsiblePersons(CancellationToken cancellationToken)
    {
        var groupId = _configuration["ManufactureGroupId"];
        if (string.IsNullOrEmpty(groupId))
        {
            return BadRequest("Manufacture group ID not configured");
        }

        var request = new GetGroupMembersRequest { GroupId = groupId };
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }
}