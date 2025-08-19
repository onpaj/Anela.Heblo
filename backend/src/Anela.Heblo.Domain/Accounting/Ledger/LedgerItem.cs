namespace Anela.Heblo.Domain.Accounting.Ledger;

public class LedgerItem
{
    public DateTime Date { get; set; }
    public string DocumentNumber { get; set; }
    public string ClientName { get; set; }
    public string VariableSymbol { get; set; }
    public string DebitAccountNumber { get; set; }
    public string DebitAccountName { get; set; }
    public string CreditAccountNumber { get; set; }
    public string CreditAccountName { get; set; }
    public string Department { get; set; }
    public decimal Amount { get; set; }
}