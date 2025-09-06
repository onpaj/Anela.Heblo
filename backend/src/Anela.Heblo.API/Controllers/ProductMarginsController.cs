using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductMargins;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProductMarginsController : BaseApiController
{
    private readonly IMediator _mediator;

    public ProductMarginsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetProductMarginsResponse>> GetProductMargins([FromQuery] GetProductMarginsRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }
}