using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP tools for Manufacture Batch calculations.
/// Provides read-only batch calculation tools for production planning.
/// </summary>
[McpServerToolType]
public class ManufactureBatchMcpTools
{
    private readonly IMediator _mediator;

    public ManufactureBatchMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    [McpServerTool]
    public async Task<string> GetBatchTemplate(
        [Description("Product code to get batch template for")]
        string productCode,
        CancellationToken cancellationToken = default
    )
    {
        var request = new CalculatedBatchSizeRequest { ProductCode = productCode };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response);
    }

    [McpServerTool]
    public async Task<string> CalculateBatchBySize(
        [Description("Product code to calculate batch for")]
        string productCode,
        [Description("Desired batch size")]
        double desiredBatchSize,
        CancellationToken cancellationToken = default
    )
    {
        var request = new CalculatedBatchSizeRequest { ProductCode = productCode, DesiredBatchSize = desiredBatchSize };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response);
    }

    [McpServerTool]
    public async Task<string> CalculateBatchByIngredient(
        [Description("Product code to calculate batch for")]
        string productCode,
        [Description("Ingredient code to scale by")]
        string ingredientCode,
        [Description("Available quantity of the ingredient")]
        double desiredIngredientAmount,
        CancellationToken cancellationToken = default
    )
    {
        var request = new CalculateBatchByIngredientRequest
        {
            ProductCode = productCode,
            IngredientCode = ingredientCode,
            DesiredIngredientAmount = desiredIngredientAmount
        };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response);
    }

    [McpServerTool]
    public async Task<string> CalculateBatchPlan(
        [Description("Batch plan request with products and quantities")]
        CalculateBatchPlanRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response);
    }
}
