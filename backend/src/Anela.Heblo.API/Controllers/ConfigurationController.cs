using Anela.Heblo.Application.Features.Configuration;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigurationController : ControllerBase
{
    private readonly IMediator _mediator;

    public ConfigurationController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<GetConfigurationResponse> GetConfiguration(CancellationToken cancellationToken)
    {
        var request = new GetConfigurationRequest();
        return await _mediator.Send(request, cancellationToken);
    }
}