using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.FinancialOverview.Model;

public class FinancialComparisonMetadataDto
{
    [Required]
    public DateTime CutoffDate { get; set; }

    [Required]
    public int AnchorYear { get; set; }

    [Required]
    public int PartialMonth { get; set; }

    [Required]
    public int PartialDayOfMonth { get; set; }

    [Required]
    public List<int> Years { get; set; } = new();

    [Required]
    public bool IncludeStockData { get; set; }

    [Required]
    public bool PartialMonthIncluded { get; set; }

    [Required]
    public int LagDays { get; set; }
}
