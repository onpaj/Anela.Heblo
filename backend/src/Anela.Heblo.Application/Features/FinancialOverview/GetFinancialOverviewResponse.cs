using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.FinancialOverview.Model;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public class GetFinancialOverviewResponse
{
    [Required]
    public List<MonthlyFinancialDataDto> Data { get; set; } = new();

    [Required]
    public FinancialSummaryDto Summary { get; set; } = new();
}