using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/leaflet")]
[Authorize]
public class LeafletController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<LeafletController> _logger;

    public LeafletController(IMediator mediator, ILogger<LeafletController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("generate")]
    [ProducesResponseType(typeof(GenerateLeafletResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    [ProducesResponseType(typeof(ProblemDetails), 502)]
    public async Task<IActionResult> Generate([FromBody] GenerateLeafletRequest request, CancellationToken ct)
    {
        try
        {
            var response = await _mediator.Send(request, ct);
            return Ok(response);
        }
        catch (EmptyRetrievalException ex)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Status = 422,
                Title = "Insufficient knowledge",
                Detail = ex.Message,
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Leaflet generation failed");
            return StatusCode(502, new ProblemDetails
            {
                Status = 502,
                Title = "Generation failed",
                Detail = "Leaflet generation failed. Please try again.",
            });
        }
    }
}
