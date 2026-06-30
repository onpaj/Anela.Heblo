using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackingStatistics;

public class GetPackingStatisticsRequest : IRequest<GetPackingStatisticsResponse>
{
    /// <summary>Inclusive start of the local-day window. Defaults to 29 days before <see cref="ToDate"/>.</summary>
    public DateTime? FromDate { get; set; }

    /// <summary>Inclusive end of the local-day window. Defaults to today (local).</summary>
    public DateTime? ToDate { get; set; }
}
