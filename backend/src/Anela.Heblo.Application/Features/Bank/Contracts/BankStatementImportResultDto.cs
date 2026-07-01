namespace Anela.Heblo.Application.Features.Bank.Contracts;

public class BankStatementImportResultDto
{
    public List<BankStatementImportDto> Statements { get; set; } = new List<BankStatementImportDto>();
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public int SkippedCount { get; set; }
    public int TotalCount => Statements.Count;
    public bool HasErrors => ErrorCount > 0;
}
