namespace Anela.Heblo.Application.Domain.Bank;

public class BankAccountSettings
{
    public static string ConfigurationKey { get; set; } = "BankAccounts";

    public List<BankAccountConfiguration> Accounts { get; set; }
}