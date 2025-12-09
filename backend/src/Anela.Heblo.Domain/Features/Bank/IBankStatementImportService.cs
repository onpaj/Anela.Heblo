using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Domain.Features.Bank;

public interface IBankStatementImportService
{
    Task<Result<bool>> ImportStatementAsync(int accountId, string statementData);
}