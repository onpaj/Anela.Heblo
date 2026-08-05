using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.API.MCP;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
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
    private readonly ICurrentUserService _currentUserService;

    public ManufactureOrderMcpTools(IMediator mediator, ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _currentUserService = currentUserService;
    }

    [McpServerTool]
    public async Task<string> GetManufactureOrders(CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Manufacture_ManufactureOrders, "Manufacture Orders");

        var request = new GetManufactureOrdersRequest();
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response, McpJsonOptions.Default);
    }

    [McpServerTool]
    public async Task<string> GetManufactureOrder(
        [Description("Manufacture order ID")]
        int id,
        CancellationToken cancellationToken = default
    )
    {
        _currentUserService.EnsureFeatureAccess(Feature.Manufacture_ManufactureOrders, "Manufacture Orders");

        var request = new GetManufactureOrderRequest { Id = id };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response, McpJsonOptions.Default);
    }

    [McpServerTool]
    public async Task<string> GetCalendarView(CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Manufacture_ManufactureOrders, "Manufacture Orders");

        var request = new GetCalendarViewRequest();
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response, McpJsonOptions.Default);
    }
}
