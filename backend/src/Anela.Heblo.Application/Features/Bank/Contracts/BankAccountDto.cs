namespace Anela.Heblo.Application.Features.Bank.Contracts;

public class BankAccountDto
{
    public string Name { get; set; } = null!;
    public string AccountNumber { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public string Currency { get; set; } = null!;
}
