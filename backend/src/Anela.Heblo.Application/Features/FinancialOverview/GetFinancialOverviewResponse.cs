using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.FinancialOverview.Model;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public class GetFinancialOverviewResponse : BaseResponse
{
    [Required]
    public List<MonthlyFinancialDataDto> Data { get; set; } = new();

    [Required]
    public FinancialSummaryDto Summary { get; set; } = new();
}