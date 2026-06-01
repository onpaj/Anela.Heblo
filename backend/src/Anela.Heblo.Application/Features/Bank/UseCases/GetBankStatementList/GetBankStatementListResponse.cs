using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

public class GetBankStatementListResponse : BaseResponse
{
    public List<BankStatementImportDto> Items { get; set; } = new List<BankStatementImportDto>();
    public int TotalCount { get; set; }
}