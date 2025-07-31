namespace Anela.Heblo.Application.Domain.Bank;

public interface IBankClient
{
    Task<BankStatementData> GetStatementAsync(string statementId);
    Task<IList<BankStatementHeader>> GetStatementsAsync(string accountNumber, DateTime requestStatementDate);
}