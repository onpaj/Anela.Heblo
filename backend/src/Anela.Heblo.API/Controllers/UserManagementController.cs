using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[GateOn(Feature.Admin_Administration)]
[ApiController]
[Route("api/[controller]")]
public class UserManagementController : BaseApiController
{
    private readonly IMediator _mediator;

    public UserManagementController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get members of a Microsoft Entra ID group.
    /// </summary>
    [HttpGet("group-members")]
    [ProducesResponseType(typeof(GetGroupMembersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<GetGroupMembersResponse>> GetGroupMembers(
        [FromQuery, Required] string groupId,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetGroupMembersRequest { GroupId = groupId }, cancellationToken);
        return HandleResponse(response);
    }
}
