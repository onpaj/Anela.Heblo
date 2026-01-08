using Anela.Heblo.Application.Features.Bank.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;

public class ImportBankStatementRequest : IRequest<ImportBankStatementResponse>
{
    public string AccountName { get; set; } = null!;
    public DateTime StatementDate { get; set; }

    public ImportBankStatementRequest(string accountName, DateTime statementDate)
    {
        AccountName = accountName;
        StatementDate = statementDate;
    }
}