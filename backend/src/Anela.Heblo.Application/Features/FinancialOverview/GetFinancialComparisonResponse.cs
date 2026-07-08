using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.FinancialOverview.Model;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public class GetFinancialComparisonResponse : BaseResponse
{
    /// <summary>One series per year, descending by year (anchor year first).</summary>
    [Required]
    public List<YearComparisonSeriesDto> Series { get; set; } = new();

    [Required]
    public FinancialComparisonMetadataDto Metadata { get; set; } = new();
}
