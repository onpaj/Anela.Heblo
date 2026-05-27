using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteEan;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetEanByCode;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListEans;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[Route("api/eans")]
[ApiController]
public class EansController : BaseApiController
{
    private readonly IMediator _mediator;

    public EansController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<ListEansResponse>> GetEans(
        [FromQuery] int? lotId,
        [FromQuery] string? materialCode,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var request = new ListEansRequest { LotId = lotId, MaterialCode = materialCode, Page = page, PageSize = pageSize };
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpGet("by-code/{code}")]
    public async Task<ActionResult<GetEanByCodeResponse>> GetEanByCode(string code, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetEanByCodeRequest { Code = code }, cancellationToken);
        return HandleResponse(response);
    }

    [HttpPost]
    public async Task<ActionResult<CreateEansResponse>> CreateEans(
        [FromBody] CreateEansRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<DeleteEanResponse>> DeleteEan(int id, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new DeleteEanRequest { Id = id }, cancellationToken);
        return HandleResponse(response);
    }
}
