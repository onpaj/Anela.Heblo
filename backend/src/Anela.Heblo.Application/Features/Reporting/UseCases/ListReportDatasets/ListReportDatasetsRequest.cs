using Anela.Heblo.Application.Features.Reporting.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Reporting.UseCases.ListReportDatasets;

public class ListReportDatasetsRequest : IRequest<ListReportDatasetsResponse>
{
}

public class ListReportDatasetsResponse : BaseResponse
{
    public List<ReportDatasetSummaryDto> Datasets { get; set; } = [];

    public ListReportDatasetsResponse() : base() { }

    public ListReportDatasetsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
