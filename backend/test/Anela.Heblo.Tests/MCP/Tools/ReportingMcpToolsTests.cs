using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.Reporting.Contracts;
using Anela.Heblo.Application.Features.Reporting.UseCases.DescribeReportDataset;
using Anela.Heblo.Application.Features.Reporting.UseCases.ListReportDatasets;
using Anela.Heblo.Application.Features.Reporting.UseCases.QueryReportDataset;
using Anela.Heblo.Application.Shared;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class ReportingMcpToolsTests
{
    private readonly Mock<IMediator> _mediator = new();

    private ReportingMcpTools CreateTools() => new(_mediator.Object);

    [Fact]
    public async Task ListReportDatasets_serializes_the_response()
    {
        _mediator.Setup(m => m.Send(It.IsAny<ListReportDatasetsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListReportDatasetsResponse { Datasets = [new ReportDatasetSummaryDto { Id = "ga4.traffic_monthly" }] });

        var json = await CreateTools().ListReportDatasets();

        Assert.Contains("ga4.traffic_monthly", json);
    }

    [Fact]
    public async Task QueryReportDataset_passes_every_parameter()
    {
        QueryReportDatasetRequest? sent = null;
        _mediator.Setup(m => m.Send(It.IsAny<QueryReportDatasetRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<QueryReportDatasetResponse>, CancellationToken>((r, _) => sent = (QueryReportDatasetRequest)r)
            .ReturnsAsync(new QueryReportDatasetResponse { Dataset = "shoptet.orders_monthly", Rows = [[new DateOnly(2026, 9, 1), 42L]], RowCount = 1 });

        var json = await CreateTools().QueryReportDataset(
            "shoptet.orders_monthly",
            columns: ["order_month", "orders"],
            filters: [new ReportFilterDto { Column = "channel", Op = "eq", Value = "eshop_mo" }],
            sort: [new ReportSortDto { Column = "orders", Direction = "desc" }],
            limit: 10,
            offset: 20);

        Assert.Equal("shoptet.orders_monthly", sent!.Dataset);
        Assert.Equal(new[] { "order_month", "orders" }, sent.Columns!);
        Assert.Equal("channel", sent.Filters![0].Column);
        Assert.Equal("desc", sent.Sort![0].Direction);
        Assert.Equal(10, sent.Limit);
        Assert.Equal(20, sent.Offset);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("2026-09-01", doc.RootElement.GetProperty("Rows")[0][0].GetString());
        Assert.Equal(42, doc.RootElement.GetProperty("Rows")[0][1].GetInt64());
        Assert.False(doc.RootElement.TryGetProperty("Success", out _));
        Assert.False(doc.RootElement.TryGetProperty("ErrorCode", out _));
        Assert.False(doc.RootElement.TryGetProperty("Params", out _));
        Assert.True(doc.RootElement.TryGetProperty("HasMore", out _));
    }

    [Fact]
    public async Task Forbidden_becomes_an_McpException_naming_the_role()
    {
        _mediator.Setup(m => m.Send(It.IsAny<QueryReportDatasetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryReportDatasetResponse(ErrorCodes.Forbidden,
                new Dictionary<string, string> { ["RequiredRole"] = "reporting.accounting.read" }));

        var ex = await Assert.ThrowsAsync<McpException>(() => CreateTools().QueryReportDataset("flexi.cost_monthly"));

        Assert.Contains("[Forbidden]", ex.Message);
        Assert.Contains("reporting.accounting.read", ex.Message);
    }

    [Fact]
    public async Task Not_found_lists_suggestions_and_points_to_ListReportDatasets()
    {
        _mediator.Setup(m => m.Send(It.IsAny<DescribeReportDatasetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DescribeReportDatasetResponse(ErrorCodes.ReportingDatasetNotFound,
                new Dictionary<string, string> { ["Dataset"] = "shoptet.order", ["Suggestions"] = "shoptet.orders_monthly" }));

        var ex = await Assert.ThrowsAsync<McpException>(() => CreateTools().DescribeReportDataset("shoptet.order"));

        Assert.Contains("[ReportingDatasetNotFound]", ex.Message);
        Assert.Contains("shoptet.orders_monthly", ex.Message);
        Assert.Contains("ListReportDatasets", ex.Message);
    }

    [Fact]
    public async Task Invalid_query_passes_the_reason_through()
    {
        _mediator.Setup(m => m.Send(It.IsAny<QueryReportDatasetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryReportDatasetResponse(ErrorCodes.ReportingInvalidQuery,
                new Dictionary<string, string> { ["Reason"] = "Unknown column 'nope'" }));

        var ex = await Assert.ThrowsAsync<McpException>(() => CreateTools().QueryReportDataset("shoptet.orders_monthly"));

        Assert.Contains("Unknown column 'nope'", ex.Message);
    }

    [Fact]
    public async Task External_service_error_gives_a_generic_reason()
    {
        _mediator.Setup(m => m.Send(It.IsAny<QueryReportDatasetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryReportDatasetResponse(ErrorCodes.ExternalServiceError,
                new Dictionary<string, string> { ["Reason"] = "Reporting data is temporarily unavailable." }));

        var ex = await Assert.ThrowsAsync<McpException>(() => CreateTools().QueryReportDataset("shoptet.orders_monthly"));

        Assert.Contains("[ExternalServiceError]", ex.Message);
        Assert.Contains("temporarily unavailable", ex.Message);
    }

    [Fact]
    public async Task Unmapped_error_with_null_params_gives_a_generic_message()
    {
        _mediator.Setup(m => m.Send(It.IsAny<QueryReportDatasetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryReportDatasetResponse(ErrorCodes.Exception, null));

        var ex = await Assert.ThrowsAsync<McpException>(() => CreateTools().QueryReportDataset("shoptet.orders_monthly"));

        Assert.Contains("[Exception]", ex.Message);
        Assert.Contains("Reporting request failed.", ex.Message);
    }

    [Fact]
    public async Task Sdk_tool_binding_accepts_numeric_and_boolean_filter_values()
    {
        QueryReportDatasetRequest? sent = null;
        _mediator.Setup(m => m.Send(It.IsAny<QueryReportDatasetRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<QueryReportDatasetResponse>, CancellationToken>((r, _) => sent = (QueryReportDatasetRequest)r)
            .ReturnsAsync(new QueryReportDatasetResponse { Dataset = "shoptet.orders_monthly" });
        var tool = McpServerTool.Create(
            typeof(ReportingMcpTools).GetMethod(nameof(ReportingMcpTools.QueryReportDataset))!, CreateTools());
        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """
            {"dataset":"shoptet.orders_monthly","filters":[
              {"column":"orders","op":"gt","value":100},
              {"column":"order_month","op":"in","values":["2026-01",2026.5,true]},
              {"column":"x","op":"isNull","value":true}]}
            """)!;
        var server = new Mock<McpServer>();
        var context = new RequestContext<CallToolRequestParams>(server.Object, new JsonRpcRequest { Method = "tools/call" })
        {
            Params = new CallToolRequestParams { Name = "QueryReportDataset", Arguments = arguments },
        };

        var result = await tool.InvokeAsync(context, CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("100", sent!.Filters![0].Value);
        Assert.Equal(new[] { "2026-01", "2026.5", "true" }, sent.Filters[1].Values!);
        Assert.Equal("true", sent.Filters[2].Value);
    }
}
