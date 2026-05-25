using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
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

    public UserManagementMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    [McpServerTool]
    public async Task<string> GetGroupMembers(
        [Description("Microsoft Entra ID group ID to fetch members for")]
        string groupId,
        CancellationToken cancellationToken = default
    )
    {
        var request = new GetGroupMembersRequest { GroupId = groupId };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response);
    }
}
