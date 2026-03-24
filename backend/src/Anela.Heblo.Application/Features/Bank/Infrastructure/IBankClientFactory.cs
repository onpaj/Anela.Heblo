using Anela.Heblo.Domain.Features.Bank;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure;

public interface IBankClientFactory
{
    IBankClient GetClient(BankAccountConfiguration accountSettings);
}
