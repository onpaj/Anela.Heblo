using Anela.Heblo.Application.Features.Reporting.Data;
using Anela.Heblo.Application.Features.Reporting.Query;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Reporting.UseCases.QueryReportDataset;

public class QueryReportDatasetHandler : IRequestHandler<QueryReportDatasetRequest, QueryReportDatasetResponse>
{
    internal const string UnavailableReason = "Reporting data is temporarily unavailable.";

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
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Reporting query failed for dataset {Dataset}", dataset.Id);
            return new QueryReportDatasetResponse(ErrorCodes.ExternalServiceError,
                new Dictionary<string, string> { ["Reason"] = UnavailableReason });
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
        Filters = RequireNoNulls(r.Filters, "filters")
            .Select(f => new ReportFilter { Column = f.Column, Op = f.Op, Value = f.Value, Values = f.Values }).ToList(),
        Sort = RequireNoNulls(r.Sort, "sort")
            .Select(s => new ReportSort { Column = s.Column, Direction = s.Direction }).ToList(),
        Limit = r.Limit,
        Offset = r.Offset,
    };

    private static IReadOnlyList<T> RequireNoNulls<T>(IReadOnlyList<T>? items, string name) where T : class
    {
        if (items is null) return [];
        if (items.Any(i => i is null))
            throw new ReportQueryValidationException($"'{name}' must not contain null entries.");
        return items;
    }
}
