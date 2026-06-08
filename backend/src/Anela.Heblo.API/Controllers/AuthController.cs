using Anela.Heblo.Application.Features.Authorization.UseCases.GetMe;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : BaseApiController
{
    private readonly IMediator _mediator;
    public AuthController(IMediator mediator) => _mediator = mediator;

    /// <summary>Returns the current user's effective permissions for the frontend.
    /// Reachable by any authenticated user (incl. no-access/disabled) via the AuthenticatedUser policy.</summary>
    [HttpGet("me")]
    [Authorize(Policy = "AuthenticatedUser")]
    public async Task<ActionResult<GetMeResponse>> Me(CancellationToken ct)
    {
        var response = await _mediator.Send(new GetMeRequest(), ct);
        return HandleResponse(response);
    }
}
