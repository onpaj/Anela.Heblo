using Anela.Heblo.Application.Features.CarrierCooling.UseCases.GetCarrierCoolingMatrix;
using Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/carrier-cooling")]
public class CarrierCoolingController : BaseApiController
{
    private readonly IMediator _mediator;

    public CarrierCoolingController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [HttpGet]
    public async Task<ActionResult<GetCarrierCoolingMatrixResponse>> GetMatrix(
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetCarrierCoolingMatrixRequest(), cancellationToken);
        return Ok(response);
    }

    [HttpPut]
    public async Task<ActionResult<SetCarrierCoolingResponse>> SetCooling(
        [FromBody] SetCarrierCoolingRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }
}
