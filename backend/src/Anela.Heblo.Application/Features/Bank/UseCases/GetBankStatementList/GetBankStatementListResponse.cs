using Anela.Heblo.Application.Features.Bank.Contracts;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

public class GetBankStatementListResponse
{
    public List<BankStatementImportDto> Items { get; set; } = new List<BankStatementImportDto>();
    public int TotalCount { get; set; }
}