using Anela.Heblo.Application.Features.Reporting.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Reporting.UseCases.QueryReportDataset;

public class QueryReportDatasetRequest : IRequest<QueryReportDatasetResponse>
{
    public string Dataset { get; set; } = string.Empty;
    public List<string>? Columns { get; set; }
    public List<ReportFilterDto>? Filters { get; set; }
    public List<ReportSortDto>? Sort { get; set; }
    public int? Limit { get; set; }
    public int? Offset { get; set; }
}

public class QueryReportDatasetResponse : BaseResponse
{
    public string Dataset { get; set; } = string.Empty;
    public List<ReportColumnDto> Columns { get; set; } = [];
    public List<List<object?>> Rows { get; set; } = [];
    public int RowCount { get; set; }
    public bool HasMore { get; set; }
    public List<string> Caveats { get; set; } = [];
    public ReportFreshnessDto? Freshness { get; set; }

    public QueryReportDatasetResponse() : base() { }

    public QueryReportDatasetResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
