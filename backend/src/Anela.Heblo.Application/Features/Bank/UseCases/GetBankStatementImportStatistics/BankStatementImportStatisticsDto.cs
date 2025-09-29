namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementImportStatistics;

public class BankStatementImportStatisticsDto
{
    public DateTime Date { get; set; }
    public int ImportCount { get; set; }
    public int TotalItemCount { get; set; }
}