namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public class DailyInvoiceCountDto
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
    public bool IsBelowThreshold { get; set; }
}
