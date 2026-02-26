using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using MediatR;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP tools for Manufacture Batch calculations.
/// Provides read-only batch calculation tools for production planning.
/// </summary>
public class ManufactureBatchMcpTools
{
    private readonly IMediator _mediator;

    public ManufactureBatchMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "manufacture_batch_get_template",
    //     Description = "Get the batch template for a product showing the standard recipe and quantities. Use this as a starting point for batch calculations."
    // )]
    public async Task<CalculatedBatchSizeResponse> GetBatchTemplate(
        // [McpToolParameter(Description = "Product code to get batch template for", Required = true)]
        string productCode
    )
    {
        var request = new CalculatedBatchSizeRequest { ProductCode = productCode };
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
    //     Name = "manufacture_batch_calculate_by_size",
    //     Description = "Calculate batch quantities based on desired batch size. Use this to plan material requirements for a specific production quantity."
    // )]
    public async Task<CalculatedBatchSizeResponse> CalculateBatchBySize(
        // [McpToolParameter(Description = "Batch calculation request with product code and desired size", Required = true)]
        CalculatedBatchSizeRequest request
    )
    {
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
    //     Name = "manufacture_batch_calculate_by_ingredient",
    //     Description = "Calculate batch quantities based on available ingredient quantity. Use this to optimize material usage when you have a specific amount of an ingredient to use up."
    // )]
    public async Task<CalculateBatchByIngredientResponse> CalculateBatchByIngredient(
        // [McpToolParameter(Description = "Batch calculation request with ingredient and quantity", Required = true)]
        CalculateBatchByIngredientRequest request
    )
    {
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
    //     Name = "manufacture_batch_calculate_plan",
    //     Description = "Calculate a complete batch plan for multiple products. Use this for comprehensive production planning across multiple items."
    // )]
    public async Task<CalculateBatchPlanResponse> CalculateBatchPlan(
        // [McpToolParameter(Description = "Batch plan request with products and quantities", Required = true)]
        CalculateBatchPlanRequest request
    )
    {
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
