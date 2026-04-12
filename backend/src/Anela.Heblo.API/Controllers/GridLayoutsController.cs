using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GridLayoutsController : BaseApiController
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;

    public GridLayoutsController(IMediator mediator, ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _currentUserService = currentUserService;
    }

    [HttpGet("{gridKey}")]
    public async Task<ActionResult<GridLayoutDto?>> Get(string gridKey)
    {
        var request = new GetGridLayoutRequest { GridKey = gridKey };
        var response = await _mediator.Send(request);
        return Ok(response.Layout);
    }

    [HttpPut("{gridKey}")]
    public async Task<ActionResult> Save(string gridKey, [FromBody] SaveGridLayoutRequest body)
    {
        body.GridKey = gridKey;
        var response = await _mediator.Send(body);
        if (!response.Success)
            return StatusCode(500, response);
        return Ok();
    }

    [HttpDelete("{gridKey}")]
    public async Task<ActionResult> Reset(string gridKey)
    {
        var request = new ResetGridLayoutRequest { GridKey = gridKey };
        var response = await _mediator.Send(request);
        if (!response.Success)
            return StatusCode(500, response);
        return Ok();
    }
}
