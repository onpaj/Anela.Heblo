namespace Anela.Heblo.Domain.Features.Bank;

public interface IBankClient
{
    BankClientProvider Provider { get; }
    Task<BankStatementData> GetStatementAsync(string statementId);
    Task<IList<BankStatementHeader>> GetStatementsAsync(string accountNumber, DateTime dateFrom, DateTime dateTo);
}
