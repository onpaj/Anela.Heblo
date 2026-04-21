using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP tools for Manufacture Order operations.
/// Provides read-only access to manufacture order data for planning and tracking.
/// </summary>
[McpServerToolType]
public class ManufactureOrderMcpTools
{
    private readonly IMediator _mediator;

    public ManufactureOrderMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    [McpServerTool]
    public async Task<string> GetManufactureOrders(CancellationToken cancellationToken = default)
    {
        var request = new GetManufactureOrdersRequest();
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response);
    }

    [McpServerTool]
    public async Task<string> GetManufactureOrder(
        [Description("Manufacture order ID")]
        int id,
        CancellationToken cancellationToken = default
    )
    {
        var request = new GetManufactureOrderRequest { Id = id };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response);
    }

    [McpServerTool]
    public async Task<string> GetCalendarView(CancellationToken cancellationToken = default)
    {
        var request = new GetCalendarViewRequest();
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response);
    }

    [McpServerTool]
    public async Task<string> GetResponsiblePersons(
        [Description("Microsoft Entra ID group ID for manufacture team")]
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
