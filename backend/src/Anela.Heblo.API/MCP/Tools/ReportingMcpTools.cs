using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.Application.Features.Reporting.Contracts;
using Anela.Heblo.Application.Features.Reporting.UseCases.DescribeReportDataset;
using Anela.Heblo.Application.Features.Reporting.UseCases.ListReportDatasets;
using Anela.Heblo.Application.Features.Reporting.UseCases.QueryReportDataset;
using Anela.Heblo.Application.Shared;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// Reporting data (GA4, Shoptet, Flexi). Permissions are enforced in the handlers, per source —
/// these tools deliberately do not call EnsureFeatureAccess.
/// </summary>
[McpServerToolType]
public class ReportingMcpTools
{
    private readonly IMediator _mediator;

    public ReportingMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    [McpServerTool]
    [Description(
        "List Anela's reporting datasets you are allowed to read: website traffic (Google Analytics 4), e-shop orders, " +
        "customers and product sales (Shoptet), and company costs / marketing / ad spend (ABRA Flexi accounting). All are " +
        "month grain. Call this FIRST for any question about sales, revenue, orders, customers, traffic, conversion or " +
        "costs over time. Returns id, source, description, grain and how fresh the data is; then call DescribeReportDataset.")]
    public async Task<string> ListReportDatasets(CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ListReportDatasetsRequest(), cancellationToken);
        EnsureSuccess(response);
        return JsonSerializer.Serialize(response.Datasets, McpJsonOptions.Default);
    }

    [McpServerTool]
    [Description(
        "Describe one reporting dataset: its columns (name, type, meaning), period column, default sort and caveats. " +
        "ALWAYS read the caveats before presenting numbers (e.g. whether amounts include VAT, or why GA4 figures differ " +
        "from the ERP) and repeat the relevant ones to the user.")]
    public async Task<string> DescribeReportDataset(
        [Description("Dataset id exactly as returned by ListReportDatasets, e.g. 'shoptet.orders_monthly'.")] string dataset,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new DescribeReportDatasetRequest { Dataset = dataset }, cancellationToken);
        EnsureSuccess(response);
        return JsonSerializer.Serialize(response.Dataset, McpJsonOptions.Default);
    }

    [McpServerTool]
    [Description(
        "Query one reporting dataset. Returns column headers once and rows as value arrays, plus hasMore for paging. " +
        "Filter ops: eq, neq, gt, gte, lt, lte (one 'value'), in (1-100 'values'), between (2 'values', inclusive), " +
        "contains (text, case-insensitive), isNull ('value' true/false). Dates accept yyyy-MM-dd or yyyy-MM. " +
        "Always filter the period column to the months you need. limit 1-1000 (default 100); page with offset while hasMore is true.")]
    public async Task<string> QueryReportDataset(
        [Description("Dataset id from ListReportDatasets.")] string dataset,
        [Description("Optional subset of columns to return, in this order. Omit for all.")] List<string>? columns = null,
        [Description("Optional filters, ANDed together.")] List<ReportFilterDto>? filters = null,
        [Description("Optional sort, e.g. [{\"column\":\"revenue_with_vat_czk\",\"direction\":\"desc\"}]. Omit for the dataset default.")] List<ReportSortDto>? sort = null,
        [Description("Max rows, 1-1000, default 100.")] int? limit = null,
        [Description("Rows to skip for paging, default 0.")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new QueryReportDatasetRequest
        {
            Dataset = dataset,
            Columns = columns,
            Filters = filters,
            Sort = sort,
            Limit = limit,
            Offset = offset,
        }, cancellationToken);
        EnsureSuccess(response);
        return JsonSerializer.Serialize(response, McpJsonOptions.Default);
    }

    private static void EnsureSuccess(BaseResponse response)
    {
        if (response.Success)
            return;

        var code = response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR";
        var p = response.Params ?? new Dictionary<string, string>();
        var detail = response.ErrorCode switch
        {
            ErrorCodes.Forbidden =>
                $"You do not have permission to read this data (requires {p.GetValueOrDefault("RequiredRole")}). Ask an administrator to grant it in Heblo access settings.",
            ErrorCodes.ReportingDatasetNotFound =>
                $"No dataset '{p.GetValueOrDefault("Dataset")}'." +
                (string.IsNullOrEmpty(p.GetValueOrDefault("Suggestions")) ? string.Empty : $" Did you mean: {p["Suggestions"]}?") +
                " Call ListReportDatasets for all ids.",
            ErrorCodes.ReportingInvalidQuery => p.GetValueOrDefault("Reason") ?? "Invalid query.",
            ErrorCodes.ReportingQueryTimeout =>
                "The query took too long. Narrow the period filter or add filters, then retry.",
            _ => response.FullError(),
        };
        throw new McpException($"[{code}] {detail}");
    }
}
