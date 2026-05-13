using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufacturedInventory;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufacturedInventoryItem;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufacturedInventoryItem;
using Anela.Heblo.Application.Features.Manufacture.UseCases.DeleteManufacturedInventoryItem;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;

namespace Anela.Heblo.API.Controllers;

[Authorize]
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
    public async Task<ActionResult<GetManufacturedInventoryResponse>> GetInventory([FromQuery] GetManufacturedInventoryRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    [HttpPost]
    public async Task<ActionResult<CreateManufacturedInventoryItemResponse>> CreateItem([FromBody] CreateManufacturedInventoryItemRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<UpdateManufacturedInventoryItemResponse>> UpdateItem(int id, [FromBody] UpdateManufacturedInventoryItemBody body)
    {
        var request = new UpdateManufacturedInventoryItemRequest
        {
            Id = id,
            NewAmount = body.NewAmount,
            Note = body.Note
        };
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<DeleteManufacturedInventoryItemResponse>> DeleteItem(int id, [FromQuery] string? note)
    {
        var request = new DeleteManufacturedInventoryItemRequest { Id = id, Note = note };
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    public class UpdateManufacturedInventoryItemBody
    {
        public decimal NewAmount { get; set; }
        public string? Note { get; set; }
    }
}
