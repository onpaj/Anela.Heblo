using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureSettings;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Manufacture_ManufactureOrders)]
[ApiController]
[Route("api/manufacture/settings")]
public class ManufactureSettingsController : BaseApiController
{
    private readonly IMediator _mediator;

    public ManufactureSettingsController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [HttpGet]
    public async Task<ActionResult<GetManufactureSettingsResponse>> GetSettings(CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetManufactureSettingsRequest(), cancellationToken);
        return HandleResponse(response);
    }
}
