using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementImportStatistics;

public class GetBankStatementImportStatisticsRequest : IRequest<GetBankStatementImportStatisticsResponse>
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string DateType { get; set; } = "ImportDate"; // "StatementDate" or "ImportDate"
}