using Anela.Heblo.Application.Features.Reporting.Data;
using Anela.Heblo.Application.Features.Reporting.Query;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Reporting.UseCases.QueryReportDataset;

public class QueryReportDatasetHandler : IRequestHandler<QueryReportDatasetRequest, QueryReportDatasetResponse>
{
    private readonly ICurrentUserService _user;
    private readonly IReportingDataReader _reader;
    private readonly ILogger<QueryReportDatasetHandler> _logger;

    public QueryReportDatasetHandler(ICurrentUserService user, IReportingDataReader reader, ILogger<QueryReportDatasetHandler> logger)
    {
        _user = user;
        _reader = reader;
        _logger = logger;
    }

    public async Task<QueryReportDatasetResponse> Handle(QueryReportDatasetRequest request, CancellationToken cancellationToken)
    {
        var (dataset, error, parameters) = ReportDatasetGate.Resolve(request.Dataset, _user);
        if (dataset is null)
            return new QueryReportDatasetResponse(error!.Value, parameters);

        ReportSqlCommand command;
        try
        {
            command = ReportSqlBuilder.Build(dataset, ToQuery(request));
        }
        catch (ReportQueryValidationException ex)
        {
            return new QueryReportDatasetResponse(ErrorCodes.ReportingInvalidQuery,
                new Dictionary<string, string> { ["Reason"] = ex.Message });
        }

        ReportRows rows;
        try
        {
            rows = await _reader.QueryAsync(command, cancellationToken);
        }
        catch (ReportQueryTimeoutException)
        {
            return new QueryReportDatasetResponse(ErrorCodes.ReportingQueryTimeout,
                new Dictionary<string, string> { ["Dataset"] = dataset.Id });
        }

        var freshness = await ReportFreshnessLoader.LoadAsync(_reader, dataset.Source, _logger, cancellationToken);
        return new QueryReportDatasetResponse
        {
            Dataset = dataset.Id,
            Columns = command.OutputColumns.Select(ReportDatasetMapper.ToDto).ToList(),
            Rows = rows.Rows.Select(r => r.ToList()).ToList(),
            RowCount = rows.Rows.Count,
            HasMore = rows.HasMore,
            Caveats = dataset.Caveats.ToList(),
            Freshness = ReportDatasetMapper.ToDto(freshness),
        };
    }

    private static ReportQuery ToQuery(QueryReportDatasetRequest r) => new()
    {
        Columns = r.Columns,
        Filters = (r.Filters ?? []).Select(f => new ReportFilter { Column = f.Column, Op = f.Op, Value = f.Value, Values = f.Values }).ToList(),
        Sort = (r.Sort ?? []).Select(s => new ReportSort { Column = s.Column, Direction = s.Direction }).ToList(),
        Limit = r.Limit,
        Offset = r.Offset,
    };
}
