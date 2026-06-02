using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureSettings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/manufacture/settings")]
public class ManufactureSettingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ManufactureSettingsController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [HttpGet]
    [AllowAnonymous]
    public Task<GetManufactureSettingsResponse> GetSettings(CancellationToken cancellationToken)
        => _mediator.Send(new GetManufactureSettingsRequest(), cancellationToken);
}
