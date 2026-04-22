namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class DailySpendDto
{
    public DateOnly Date { get; set; }
    public decimal MetaSpend { get; set; }
    public decimal GoogleSpend { get; set; }
}
