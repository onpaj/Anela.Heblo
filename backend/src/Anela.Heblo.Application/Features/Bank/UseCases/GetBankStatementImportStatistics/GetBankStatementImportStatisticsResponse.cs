using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementImportStatistics;

public class GetBankStatementImportStatisticsResponse : BaseResponse
{
    public IEnumerable<BankStatementImportStatisticsDto> Statistics { get; set; } = new List<BankStatementImportStatisticsDto>();
}

public class BankStatementImportStatisticsDto
{
    public DateTime Date { get; set; }
    public int ImportCount { get; set; }
    public int TotalItemCount { get; set; }
}