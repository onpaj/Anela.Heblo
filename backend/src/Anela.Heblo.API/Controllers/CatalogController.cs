using Microsoft.AspNetCore.Mvc;
using MediatR;
using Anela.Heblo.Application.features.catalog.contracts;
using Microsoft.AspNetCore.Authorization;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CatalogController : ControllerBase
{
    private readonly IMediator _mediator;

    public CatalogController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetCatalogListResponse>> GetCatalogList([FromQuery] GetCatalogListRequest request)
    {
        var response = await _mediator.Send(request);
        return Ok(response);
    }
}