namespace Anela.Heblo.Domain.Features.Bank;

public class BankStatementImportStatistics
{
    public DateTime Date { get; set; }
    public int ImportCount { get; set; }
    public int TotalItemCount { get; set; }
}