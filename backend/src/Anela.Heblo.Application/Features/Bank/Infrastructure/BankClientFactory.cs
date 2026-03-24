using Anela.Heblo.Domain.Features.Bank;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure;

public class BankClientFactory : IBankClientFactory
{
    private readonly IEnumerable<IBankClient> _clients;

    public BankClientFactory(IEnumerable<IBankClient> clients)
        => _clients = clients;

    public IBankClient GetClient(BankAccountConfiguration accountSettings)
    {
        return _clients.SingleOrDefault(c => c.Provider == accountSettings.Provider)
            ?? throw new InvalidOperationException(
                $"No bank client registered for provider '{accountSettings.Provider}'");
    }
}
