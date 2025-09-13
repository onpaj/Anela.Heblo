using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufactureOrder;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace Anela.Heblo.API.Controllers;

[Authorize]
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
    public async Task<ActionResult<CreateManufactureOrderResponse>> CreateOrder([FromBody] CreateManufactureOrderRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }
}