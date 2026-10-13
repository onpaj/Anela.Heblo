using Anela.Heblo.Application.Features.Reporting.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Reporting.UseCases.DescribeReportDataset;

public class DescribeReportDatasetRequest : IRequest<DescribeReportDatasetResponse>
{
    public string Dataset { get; set; } = string.Empty;
}

public class DescribeReportDatasetResponse : BaseResponse
{
    public ReportDatasetDetailDto? Dataset { get; set; }

    public DescribeReportDatasetResponse() : base() { }

    public DescribeReportDatasetResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
