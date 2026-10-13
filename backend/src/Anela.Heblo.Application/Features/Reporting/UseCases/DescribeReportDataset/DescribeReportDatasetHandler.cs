using Anela.Heblo.Application.Features.Reporting.Data;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Reporting.UseCases.DescribeReportDataset;

public class DescribeReportDatasetHandler : IRequestHandler<DescribeReportDatasetRequest, DescribeReportDatasetResponse>
{
    private readonly ICurrentUserService _user;
    private readonly IReportingDataReader _reader;
    private readonly ILogger<DescribeReportDatasetHandler> _logger;

    public DescribeReportDatasetHandler(ICurrentUserService user, IReportingDataReader reader, ILogger<DescribeReportDatasetHandler> logger)
    {
        _user = user;
        _reader = reader;
        _logger = logger;
    }

    public async Task<DescribeReportDatasetResponse> Handle(DescribeReportDatasetRequest request, CancellationToken cancellationToken)
    {
        var (dataset, error, parameters) = ReportDatasetGate.Resolve(request.Dataset, _user);
        if (dataset is null)
            return new DescribeReportDatasetResponse(error!.Value, parameters);

        var freshness = await ReportFreshnessLoader.LoadAsync(_reader, dataset.Source, _logger, cancellationToken);
        return new DescribeReportDatasetResponse { Dataset = ReportDatasetMapper.ToDetail(dataset, freshness) };
    }
}
