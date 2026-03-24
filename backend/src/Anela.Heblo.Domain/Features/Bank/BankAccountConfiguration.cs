using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Domain.Features.Bank;

public class BankAccountConfiguration
{
    public string Name { get; set; } = null!;
    public BankClientProvider Provider { get; set; }
    public string AccountNumber { get; set; } = null!;
    public int FlexiBeeId { get; set; }
    public CurrencyCode Currency { get; set; }
}
