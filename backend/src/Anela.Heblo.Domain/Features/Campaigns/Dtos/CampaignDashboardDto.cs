namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class CampaignDashboardDto
{
    public decimal TotalSpend { get; set; }
    public long TotalConversions { get; set; }
    public decimal AvgRoas { get; set; }
    public decimal AvgCpc { get; set; }
    public IReadOnlyList<DailySpendDto> SpendOverTime { get; set; } = [];
}
