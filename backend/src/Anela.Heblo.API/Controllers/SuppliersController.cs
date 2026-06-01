using Anela.Heblo.Application.Features.Purchase.UseCases.SearchSuppliers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.API.Infrastructure;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/suppliers")]
public class SuppliersController : ControllerBase
{
    private readonly IMediator _mediator;

    public SuppliersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("search")]
    public async Task<ActionResult<SearchSuppliersResponse>> SearchSuppliers(
        [FromQuery] SearchSuppliersRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ErrorResponseHelper.CreateValidationError<SearchSuppliersResponse>());
        }

        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }
}