using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufacturedInventory;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufacturedInventoryItem;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufacturedInventoryItem;
using Anela.Heblo.Application.Features.Manufacture.UseCases.DeleteManufacturedInventoryItem;
using Anela.Heblo.Domain.Features.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;

namespace Anela.Heblo.API.Controllers;

[GateOn(Feature.Manufacture_ProductInventory)]
[Authorize(Roles = AccessRoles.ManufactureProductInventoryRead)]
[ApiController]
[Route("api/manufactured-inventory")]
public class ManufacturedProductInventoryController : BaseApiController
{
    private readonly IMediator _mediator;

    public ManufacturedProductInventoryController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetManufacturedInventoryResponse>> GetInventory([FromQuery] GetManufacturedInventoryRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpPost]
    [Authorize(Roles = AccessRoles.ManufactureProductInventoryWrite)]
    public async Task<ActionResult<CreateManufacturedInventoryItemResponse>> CreateItem([FromBody] CreateManufacturedInventoryItemRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = AccessRoles.ManufactureProductInventoryWrite)]
    public async Task<ActionResult<UpdateManufacturedInventoryItemResponse>> UpdateItem(int id, [FromBody] UpdateManufacturedInventoryItemBody body, CancellationToken cancellationToken = default)
    {
        var request = new UpdateManufacturedInventoryItemRequest
        {
            Id = id,
            NewAmount = body.NewAmount,
            Note = body.Note
        };
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = AccessRoles.ManufactureProductInventoryWrite)]
    public async Task<ActionResult<DeleteManufacturedInventoryItemResponse>> DeleteItem(int id, [FromQuery] string? note, CancellationToken cancellationToken = default)
    {
        var request = new DeleteManufacturedInventoryItemRequest { Id = id, Note = note };
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }
}
