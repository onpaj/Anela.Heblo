using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Data;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Reporting.UseCases.ListReportDatasets;

public class ListReportDatasetsHandler : IRequestHandler<ListReportDatasetsRequest, ListReportDatasetsResponse>
{
    private readonly ICurrentUserService _user;
    private readonly IReportingDataReader _reader;
    private readonly ILogger<ListReportDatasetsHandler> _logger;

    public ListReportDatasetsHandler(ICurrentUserService user, IReportingDataReader reader, ILogger<ListReportDatasetsHandler> logger)
    {
        _user = user;
        _reader = reader;
        _logger = logger;
    }

    public async Task<ListReportDatasetsResponse> Handle(ListReportDatasetsRequest request, CancellationToken cancellationToken)
    {
        var readable = Enum.GetValues<ReportSource>().Where(s => ReportAccess.CanRead(_user, s)).ToList();

        var freshness = new Dictionary<ReportSource, ReportFreshness>();
        foreach (var source in readable)
            freshness[source] = await ReportFreshnessLoader.LoadAsync(_reader, source, _logger, cancellationToken);

        return new ListReportDatasetsResponse
        {
            Datasets = ReportDatasetCatalog.All
                .Where(d => freshness.ContainsKey(d.Source))
                .Select(d => ReportDatasetMapper.ToSummary(d, freshness[d.Source]))
                .ToList(),
        };
    }
}
