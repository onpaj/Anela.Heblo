namespace Anela.Heblo.Application.Features.Bank.Contracts;

public class BankStatementImportResultDto
{
    public List<BankStatementImportDto> Statements { get; set; } = new List<BankStatementImportDto>();
}