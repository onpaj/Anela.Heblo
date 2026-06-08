using Anela.Heblo.Application.Features.Authorization.UseCases.AddGroupMember;
using Anela.Heblo.Application.Features.Authorization.UseCases.AssignUserGroups;
using Anela.Heblo.Application.Features.Authorization.UseCases.CreateGroup;
using Anela.Heblo.Application.Features.Authorization.UseCases.DeleteGroup;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetGroupDetail;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetGroups;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetPermissionCatalogue;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetUserEffectivePermissions;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetUsers;
using Anela.Heblo.Application.Features.Authorization.UseCases.SetUserActive;
using Anela.Heblo.Application.Features.Authorization.UseCases.UpdateGroup;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[GateOn(Feature.Admin_Administration)]
[Authorize(Roles = AccessRoles.AdminAdministrationRead)]
[ApiController]
[Route("api/admin/authorization")]
public class AuthorizationController : BaseApiController
{
    private readonly IMediator _mediator;
    public AuthorizationController(IMediator mediator) => _mediator = mediator;

    [HttpGet("catalogue")]
    public async Task<ActionResult<GetPermissionCatalogueResponse>> Catalogue(CancellationToken ct)
        => HandleResponse(await _mediator.Send(new GetPermissionCatalogueRequest(), ct));

    [HttpGet("groups")]
    public async Task<ActionResult<GetGroupsResponse>> GetGroups(CancellationToken ct)
        => HandleResponse(await _mediator.Send(new GetGroupsRequest(), ct));

    [HttpGet("groups/{id:guid}")]
    public async Task<ActionResult<GetGroupDetailResponse>> GetGroup([FromRoute] Guid id, CancellationToken ct)
        => HandleResponse(await _mediator.Send(new GetGroupDetailRequest { Id = id }, ct));

    [HttpPost("groups")]
    [Authorize(Roles = AccessRoles.AdminAdministrationWrite)]
    public async Task<ActionResult<CreateGroupResponse>> CreateGroup([FromBody] CreateGroupRequest request, CancellationToken ct)
        => HandleResponse(await _mediator.Send(request, ct));

    [HttpPut("groups/{id:guid}")]
    [Authorize(Roles = AccessRoles.AdminAdministrationWrite)]
    public async Task<ActionResult<UpdateGroupResponse>> UpdateGroup([FromRoute] Guid id, [FromBody] UpdateGroupRequest request, CancellationToken ct)
    {
        request.Id = id;
        return HandleResponse(await _mediator.Send(request, ct));
    }

    [HttpDelete("groups/{id:guid}")]
    [Authorize(Roles = AccessRoles.AdminAdministrationWrite)]
    public async Task<ActionResult<DeleteGroupResponse>> DeleteGroup([FromRoute] Guid id, CancellationToken ct)
        => HandleResponse(await _mediator.Send(new DeleteGroupRequest { Id = id }, ct));

    [HttpPost("groups/{id:guid}/members")]
    [Authorize(Roles = AccessRoles.AdminAdministrationWrite)]
    public async Task<ActionResult<AddGroupMemberResponse>> AddGroupMember([FromRoute] Guid id, [FromBody] AddGroupMemberRequest request, CancellationToken ct)
    {
        request.GroupId = id;
        return HandleResponse(await _mediator.Send(request, ct));
    }

    [HttpGet("users")]
    public async Task<ActionResult<GetUsersResponse>> GetUsers(CancellationToken ct)
        => HandleResponse(await _mediator.Send(new GetUsersRequest(), ct));

    [HttpGet("entra-users")]
    public async Task<ActionResult<GetEntraAccessUsersResponse>> GetEntraUsers(CancellationToken ct)
        => HandleResponse(await _mediator.Send(new GetEntraAccessUsersRequest(), ct));

    [HttpGet("users/{id:guid}/permissions")]
    public async Task<ActionResult<GetUserEffectivePermissionsResponse>> GetUserPermissions([FromRoute] Guid id, CancellationToken ct)
        => HandleResponse(await _mediator.Send(new GetUserEffectivePermissionsRequest { UserId = id }, ct));

    [HttpPut("users/{id:guid}/groups")]
    [Authorize(Roles = AccessRoles.AdminAdministrationWrite)]
    public async Task<ActionResult<AssignUserGroupsResponse>> AssignGroups([FromRoute] Guid id, [FromBody] AssignUserGroupsRequest request, CancellationToken ct)
    {
        request.UserId = id;
        return HandleResponse(await _mediator.Send(request, ct));
    }

    [HttpPut("users/{id:guid}/active")]
    [Authorize(Roles = AccessRoles.AdminAdministrationWrite)]
    public async Task<ActionResult<SetUserActiveResponse>> SetActive([FromRoute] Guid id, [FromBody] SetUserActiveRequest request, CancellationToken ct)
    {
        request.UserId = id;
        return HandleResponse(await _mediator.Send(request, ct));
    }
}
