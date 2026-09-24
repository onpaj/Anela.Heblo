using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.API.MCP;
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingBaseline;
using Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingScenario;
using Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingScenarios;
using Anela.Heblo.Application.Features.Pricing.UseCases.RecalculatePricing;
using Anela.Heblo.Application.Features.Pricing.UseCases.SavePricingScenario;
using Anela.Heblo.Application.Features.Pricing.UseCases.UpdatePricingScenarioProducts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Users;
using FluentValidation;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP tools for the Pricing Simulator (what-if price / cost / margin scenarios).
/// Thin wrappers around the same MediatR handlers as PricingSimulatorController and
/// PricingScenariosController, gated by the same Finance_PriceAnalysis permission.
/// The simulation is stateless: the caller carries the sparse override list between calls.
/// </summary>
[McpServerToolType]
public class PricingSimulatorMcpTools
{
    private const string ResourceName = "Pricing Simulator";

    private const string FilterDescription =
        "Keep the same filters across calls: overrides for products outside the filter are ignored and edits for them are rejected.";

    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;

    public PricingSimulatorMcpTools(IMediator mediator, ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _currentUserService = currentUserService;
    }

    [McpServerTool]
    [Description("Current pricing state (no simulated changes) per product: price without VAT, material cost, manufacturing cost, " +
                 "trailing 12-month sold quantity, M0 (price - material) and M1 (M0 - manufacturing) in CZK and %, plus portfolio totals. " +
                 "Without a productType filter it covers Product and Goods. Rows with IsExcluded=true lack price or cost data.")]
    public async Task<string> GetPricingBaseline(
        [Description("Filter by product code (partial match)")]
        string? productCode = null,
        [Description("Filter by product name (partial match)")]
        string? productName = null,
        [Description("Filter by product type (Product, Goods, Set, ...)")]
        ProductType? productType = null,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Finance_PriceAnalysis, ResourceName);

        var request = new GetPricingBaselineRequest
        {
            ProductCode = productCode,
            ProductName = productName,
            ProductType = productType
        };

        var response = await _mediator.Send(request, cancellationToken);
        EnsureSuccess(response);

        return JsonSerializer.Serialize(response, McpJsonOptions.Default);
    }

    [McpServerTool]
    [Description("Simulate a pricing scenario and return the recalculated rows, before/after totals (revenue, M0, M1) and the resulting override list. " +
                 "Pass the override list from the previous call (or a saved scenario) to continue from it; pass edits to change it. " +
                 "Edits are applied in order and all-or-nothing: if one fails, the error names it and none are applied. Edit fields: Price, MaterialCost, ManufacturingCost (CZK, excl. VAT), " +
                 "M0Amount, M1Amount (target margin in CZK, solved by adjusting material resp. manufacturing cost), " +
                 "M0Percentage, M1Percentage (target margin in %, e.g. 60 = 60 %), ForecastQuantity (units). " +
                 "Nothing is persisted -- use SavePricingScenario for that.")]
    public async Task<string> SimulatePricing(
        [Description("Current sparse override list (only touched products). Omit to start from the baseline.")]
        List<PricingOverrideDto>? overrides = null,
        [Description("Edits to apply, in order, on top of the overrides")]
        List<PricingEditDto>? edits = null,
        [Description("Filter by product code (partial match). " + FilterDescription)]
        string? productCode = null,
        [Description("Filter by product name (partial match). " + FilterDescription)]
        string? productName = null,
        [Description("Filter by product type. " + FilterDescription)]
        ProductType? productType = null,
        [Description("Return only rows with a simulated change (default: true). Totals always cover all filtered products.")]
        bool onlyEditedRows = true,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Finance_PriceAnalysis, ResourceName);

        // The handler takes one edit per call, so a batch is chained: each step replays the
        // overrides the previous step produced. No edits means a single plain replay.
        IReadOnlyList<PricingEditDto?> steps = edits is { Count: > 0 } ? edits : new PricingEditDto?[] { null };

        var response = new RecalculatePricingResponse { Overrides = overrides ?? new List<PricingOverrideDto>() };
        for (var i = 0; i < steps.Count; i++)
        {
            var edit = steps[i];
            var request = new RecalculatePricingRequest
            {
                ProductCode = productCode,
                ProductName = productName,
                ProductType = productType,
                Overrides = response.Overrides,
                Edit = edit
            };

            response = await SendValidated(request, cancellationToken);

            // All-or-nothing: nothing from earlier steps is returned, so the caller still
            // holds its original overrides and can resend the batch without the failing edit.
            if (!response.Success && edit is not null)
            {
                throw new McpException(
                    $"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] Edit #{i + 1} of {steps.Count} " +
                    $"({edit.ProductCode} {edit.Field}={edit.Value}) failed; no edits were applied. {response.FullError()}");
            }

            EnsureSuccess(response);
        }

        return JsonSerializer.Serialize(new
        {
            Rows = SelectRows(response.Rows, onlyEditedRows),
            response.Totals,
            response.Overrides
        }, McpJsonOptions.Default);
    }

    [McpServerTool]
    [Description("List saved pricing scenarios (id, name, description, author, dates, number of edited products).")]
    public async Task<string> ListPricingScenarios(CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Finance_PriceAnalysis, ResourceName);

        var response = await _mediator.Send(new GetPricingScenariosRequest(), cancellationToken);
        EnsureSuccess(response);

        return JsonSerializer.Serialize(response, McpJsonOptions.Default);
    }

    [McpServerTool]
    [Description("Load a saved pricing scenario recalculated against today's catalog: summary, rows, totals and its override list. " +
                 "Rows with BaselineDrifted=true have a price or cost that changed since the scenario was saved. " +
                 "Pass the returned overrides to SimulatePricing to continue editing it.")]
    public async Task<string> GetPricingScenario(
        [Description("Scenario id (from ListPricingScenarios)")]
        Guid id,
        [Description("Return only rows with a simulated change (default: true). Totals always cover all products in the scenario's filter.")]
        bool onlyEditedRows = true,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Finance_PriceAnalysis, ResourceName);

        var response = await _mediator.Send(new GetPricingScenarioRequest { Id = id }, cancellationToken);
        EnsureSuccess(response);

        return JsonSerializer.Serialize(new
        {
            response.Scenario,
            Rows = SelectRows(response.Rows, onlyEditedRows),
            response.Totals,
            response.Overrides
        }, McpJsonOptions.Default);
    }

    [McpServerTool]
    [Description("Save a pricing scenario (its override list, typically the one returned by SimulatePricing). " +
                 "Without id a new scenario is created; with id the existing scenario is overwritten as a whole " +
                 "(name, description, filter and the full override list) -- use UpdatePricingScenarioProducts to change only some products. Requires write access.")]
    public async Task<string> SavePricingScenario(
        [Description("Scenario name (max 200 characters, unique)")]
        string name,
        [Description("Sparse override list to store")]
        List<PricingOverrideDto> overrides,
        [Description("Id of the scenario to overwrite; omit to create a new one")]
        Guid? id = null,
        [Description("Optional description (max 2000 characters)")]
        string? description = null,
        [Description("Product code filter the scenario was built with")]
        string? productCode = null,
        [Description("Product name filter the scenario was built with")]
        string? productName = null,
        [Description("Product type filter the scenario was built with")]
        ProductType? productType = null,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Finance_PriceAnalysis, ResourceName, AccessLevel.Write);

        var request = new SavePricingScenarioRequest
        {
            Id = id,
            Name = name,
            Description = description,
            ProductCode = productCode,
            ProductName = productName,
            ProductType = productType,
            Overrides = overrides
        };

        var response = await SendValidated(request, cancellationToken);
        EnsureSuccess(response);

        return JsonSerializer.Serialize(response, McpJsonOptions.Default);
    }

    [McpServerTool]
    [Description("Partially update a saved pricing scenario: edit or remove individual products and/or rename it, " +
                 "leaving every other product, the filter and unspecified metadata untouched. Removals run first, then edits in order " +
                 "(same edit fields as SimulatePricing; a Price edit keeps the product's other overrides). " +
                 "All-or-nothing: if an edit fails, nothing is saved and the error names it (editNumber). " +
                 "Returns the recalculated scenario and any removal codes that matched nothing. Requires write access.")]
    public async Task<string> UpdatePricingScenarioProducts(
        [Description("Scenario id (from ListPricingScenarios)")]
        Guid id,
        [Description("Edits to apply, in order; a product not yet in the scenario is added")]
        List<PricingEditDto>? edits = null,
        [Description("Product codes whose override is dropped, so they follow the live catalog again")]
        List<string>? removeProductCodes = null,
        [Description("New name; omit to keep the current one")]
        string? name = null,
        [Description("New description; omit to keep it, empty string to clear it")]
        string? description = null,
        [Description("Return only rows with a simulated change (default: true). Totals always cover all products in the scenario's filter.")]
        bool onlyEditedRows = true,
        CancellationToken cancellationToken = default)
    {
        _currentUserService.EnsureFeatureAccess(Feature.Finance_PriceAnalysis, ResourceName, AccessLevel.Write);

        var request = new UpdatePricingScenarioProductsRequest
        {
            ScenarioId = id,
            Edits = edits ?? new List<PricingEditDto>(),
            RemoveProductCodes = removeProductCodes ?? new List<string>(),
            Name = name,
            Description = description
        };

        var response = await SendValidated(request, cancellationToken);
        EnsureSuccess(response);

        return JsonSerializer.Serialize(new
        {
            response.Scenario,
            Rows = SelectRows(response.Rows, onlyEditedRows),
            response.Totals,
            response.Overrides,
            response.UnmatchedRemovals
        }, McpJsonOptions.Default);
    }

    private async Task<TResponse> SendValidated<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken)
    {
        try
        {
            return await _mediator.Send(request, cancellationToken);
        }
        catch (ValidationException ex)
        {
            var details = string.Join(" | ",
                ex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
            throw new McpException($"[{ErrorCodes.ValidationError}] {details}");
        }
    }

    private static void EnsureSuccess(BaseResponse response)
    {
        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }
    }

    private static List<PricingRowDto> SelectRows(IEnumerable<PricingRowDto> rows, bool onlyEditedRows) =>
        onlyEditedRows ? rows.Where(r => r.IsEdited).ToList() : rows.ToList();
}
