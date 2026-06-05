using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteLot;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLot;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListLots;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.UpdateLot;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize(Roles = AccessRoles.MaterialContainersRead)]
[Route("api/lots")]
[ApiController]
public class LotsController : BaseApiController
{
    private readonly IMediator _mediator;

    public LotsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<ListLotsResponse>> GetLots(
        [FromQuery] string? materialCode,
        [FromQuery] DateOnly? expirationFrom,
        [FromQuery] DateOnly? expirationTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var request = new ListLotsRequest
        {
            MaterialCode = materialCode,
            ExpirationFrom = expirationFrom,
            ExpirationTo = expirationTo,
            Page = page,
            PageSize = pageSize
        };
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GetLotResponse>> GetLotById(int id, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetLotRequest { Id = id }, cancellationToken);
        return HandleResponse(response);
    }

    [HttpPost]
    [Authorize(Roles = AccessRoles.MaterialContainersWrite)]
    public async Task<ActionResult<CreateLotResponse>> CreateLot(
        [FromBody] CreateLotRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AccessRoles.MaterialContainersWrite)]
    public async Task<ActionResult<UpdateLotResponse>> UpdateLot(
        int id,
        [FromBody] UpdateLotRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateLotRequest
        {
            Id = id,
            Expiration = request.Expiration,
            ReceivedDate = request.ReceivedDate,
            Notes = request.Notes
        };
        var response = await _mediator.Send(command, cancellationToken);
        return HandleResponse(response);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AccessRoles.MaterialContainersWrite)]
    public async Task<ActionResult<DeleteLotResponse>> DeleteLot(int id, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new DeleteLotRequest { Id = id }, cancellationToken);
        return HandleResponse(response);
    }
}
