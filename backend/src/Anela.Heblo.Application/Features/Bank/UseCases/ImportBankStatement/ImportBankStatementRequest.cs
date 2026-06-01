using Anela.Heblo.Application.Features.Bank.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;

public class ImportBankStatementRequest : IRequest<ImportBankStatementResponse>
{
    public string AccountName { get; set; } = null!;
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }

    public ImportBankStatementRequest(string accountName, DateTime dateFrom, DateTime dateTo)
    {
        AccountName = accountName;
        DateFrom = dateFrom;
        DateTo = dateTo;
    }
}
