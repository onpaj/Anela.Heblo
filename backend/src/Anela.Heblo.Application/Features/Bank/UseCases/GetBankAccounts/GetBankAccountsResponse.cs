using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;

public class GetBankAccountsResponse : BaseResponse
{
    public List<BankAccountDto> Accounts { get; set; } = new List<BankAccountDto>();
}
