using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using MediatR;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP tools for Manufacture Order operations.
/// Provides read-only access to manufacture order data for planning and tracking.
/// </summary>
public class ManufactureOrderMcpTools
{
    private readonly IMediator _mediator;

    public ManufactureOrderMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "manufacture_order_get_list",
    //     Description = "Get list of manufacture orders with optional filtering. Use this to see all planned and in-progress manufacturing activities."
    // )]
    public async Task<GetManufactureOrdersResponse> GetManufactureOrders()
    {
        var request = new GetManufactureOrdersRequest();
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            throw new McpToolException(
                response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
                response.FullError()
            );
        }

        return response;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "manufacture_order_get_detail",
    //     Description = "Get detailed information about a specific manufacture order including materials, timeline, and status. Use this to track progress of a manufacturing batch."
    // )]
    public async Task<GetManufactureOrderResponse> GetManufactureOrder(
        // [McpToolParameter(Description = "Manufacture order ID", Required = true)]
        int id
    )
    {
        var request = new GetManufactureOrderRequest { Id = id };
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            throw new McpToolException(
                response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
                response.FullError()
            );
        }

        return response;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "manufacture_order_get_calendar",
    //     Description = "Get calendar view of manufacture orders showing scheduled production timeline. Use this for production planning and capacity analysis."
    // )]
    public async Task<GetCalendarViewResponse> GetCalendarView()
    {
        var request = new GetCalendarViewRequest();
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            throw new McpToolException(
                response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
                response.FullError()
            );
        }

        return response;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "manufacture_order_get_responsible_persons",
    //     Description = "Get list of people who can be assigned as responsible for manufacture orders. Use this when planning order assignments."
    // )]
    public async Task<GetGroupMembersResponse> GetResponsiblePersons(
        // [McpToolParameter(Description = "Microsoft Entra ID group ID for manufacture team", Required = true)]
        string groupId
    )
    {
        var request = new GetGroupMembersRequest { GroupId = groupId };
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            throw new McpToolException(
                response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
                response.FullError()
            );
        }

        return response;
    }
}
