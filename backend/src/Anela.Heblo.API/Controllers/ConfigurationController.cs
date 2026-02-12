using Anela.Heblo.Application.Features.Configuration;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigurationController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationController> _logger;

    public ConfigurationController(
        IMediator mediator,
        IConfiguration configuration,
        ILogger<ConfigurationController> logger)
    {
        _mediator = mediator;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<GetConfigurationResponse>> GetConfiguration(CancellationToken cancellationToken)
    {
        // Validate Origin header against allowed origins
        var origin = Request.Headers["Origin"].ToString();
        if (!string.IsNullOrEmpty(origin))
        {
            var allowedOrigins = _configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
            var isAllowed = allowedOrigins.Any(allowed => allowed.Equals(origin, StringComparison.OrdinalIgnoreCase));

            if (!isAllowed)
            {
                _logger.LogWarning("Configuration request blocked from disallowed origin: {Origin}", origin);
                return Forbid();
            }

            _logger.LogDebug("Configuration request allowed from origin: {Origin}", origin);
        }

        var request = new GetConfigurationRequest();
        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }
}