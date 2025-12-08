using Anela.Heblo.Application.Features.Bank.Contracts;

namespace Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;

public class ImportBankStatementResponse
{
    public List<BankStatementImportDto> Statements { get; set; } = new List<BankStatementImportDto>();
}