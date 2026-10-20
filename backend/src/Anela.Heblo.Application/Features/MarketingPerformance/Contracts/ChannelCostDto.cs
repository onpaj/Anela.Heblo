using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Contracts;

public class ChannelCostDto
{
    [Required] public string ChannelCode { get; set; } = string.Empty;
    [Required] public string Label { get; set; } = string.Empty;
    [Required] public decimal CostWithoutVat { get; set; }
    [Required] public int InvoiceCount { get; set; }
}
