namespace Anela.Heblo.Domain.Features.Bank;

public class BankAccountSettings
{
    public const string ConfigurationKey = "BankAccounts";

    public List<BankAccountConfiguration> Accounts { get; set; }
}