using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.API.MCP;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using FluentValidation;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP tools for user-directory lookups against Microsoft Entra ID.
/// </summary>
[McpServerToolType]
public class UserManagementMcpTools
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;

    public UserManagementMcpTools(IMediator mediator, ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _currentUserService = currentUserService;
    }

    [McpServerTool]
    public async Task<string> GetGroupMembers(
        [Description("Microsoft Entra ID group ID to fetch members for")]
        string groupId,
        CancellationToken cancellationToken = default
    )
    {
        _currentUserService.EnsureFeatureAccess(Feature.Admin_Administration, "User Management");

        var request = new GetGroupMembersRequest { GroupId = groupId };

        GetGroupMembersResponse response;
        try
        {
            response = await _mediator.Send(request, cancellationToken);
        }
        catch (ValidationException ex)
        {
            var details = string.Join(" | ",
                ex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
            throw new McpException($"[{ErrorCodes.ValidationError}] {details}");
        }

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response, McpJsonOptions.Default);
    }
}
