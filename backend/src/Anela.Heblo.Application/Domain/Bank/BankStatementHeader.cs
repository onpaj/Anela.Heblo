namespace Anela.Heblo.Application.Domain.Bank;

public class BankStatementHeader
{
    public string StatementId { get; set; }
    public DateTime Date { get; set; }
    public string Account { get; set; }
}