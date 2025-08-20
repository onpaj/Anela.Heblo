using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.FinancialOverview.Model;

public class GetFinancialOverviewResponse
{
    [Required]
    public List<MonthlyFinancialDataDto> Data { get; set; } = new();

    [Required]
    public FinancialSummaryDto Summary { get; set; } = new();
}