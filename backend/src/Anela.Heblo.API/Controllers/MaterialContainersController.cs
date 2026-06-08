using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DiscardMaterialContainer;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLastUsedLotForMaterial;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetMaterialContainerByCode;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListMaterialContainers;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintMaterialContainerLabels;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[GateOn(Feature.Manufacture_MaterialContainers)]
[Authorize(Roles = AccessRoles.ManufactureMaterialContainersRead)]
[Route("api/material-containers")]
[ApiController]
public class MaterialContainersController : BaseApiController
{
    private readonly IMediator _mediator;

    public MaterialContainersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<ListMaterialContainersResponse>> GetMaterialContainers(
        [FromQuery] string? materialCode,
        [FromQuery] string? lotCode,
        [FromQuery] string? code,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var request = new ListMaterialContainersRequest
        { MaterialCode = materialCode, LotCode = lotCode, Code = code, Page = page, PageSize = pageSize };
        return HandleResponse(await _mediator.Send(request, cancellationToken));
    }

    [HttpGet("last-used-lot")]
    public async Task<ActionResult<GetLastUsedLotForMaterialResponse>> GetLastUsedLot(
        [FromQuery] string materialCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            return BadRequest();
        var response = await _mediator.Send(
            new GetLastUsedLotForMaterialRequest { MaterialCode = materialCode }, cancellationToken);
        return HandleResponse(response);
    }

    [HttpGet("by-code/{code}")]
    public async Task<ActionResult<GetMaterialContainerByCodeResponse>> GetByCode(string code, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetMaterialContainerByCodeRequest { Code = code }, cancellationToken);
        return HandleResponse(response);
    }

    [HttpPost]
    [Authorize(Roles = AccessRoles.ManufactureMaterialContainersWrite)]
    public async Task<ActionResult<CreateMaterialContainersResponse>> Create(
        [FromBody] CreateMaterialContainersRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpPost("{id:int}/discard")]
    [Authorize(Roles = AccessRoles.ManufactureMaterialContainersWrite)]
    public async Task<ActionResult<DiscardMaterialContainerResponse>> Discard(int id, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new DiscardMaterialContainerRequest { Id = id }, cancellationToken);
        return HandleResponse(response);
    }

    [HttpPost("print-labels")]
    [Authorize(Roles = AccessRoles.ManufactureMaterialContainersWrite)]
    public async Task<ActionResult<PrintMaterialContainerLabelsResponse>> PrintLabels(
        [FromBody] PrintMaterialContainerLabelsRequest request, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }
}
