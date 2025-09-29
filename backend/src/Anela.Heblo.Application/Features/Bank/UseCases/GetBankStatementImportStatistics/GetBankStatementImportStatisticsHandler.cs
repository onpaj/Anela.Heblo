using Anela.Heblo.Domain.Features.Bank;
using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementImportStatistics;

public class GetBankStatementImportStatisticsHandler : IRequestHandler<GetBankStatementImportStatisticsRequest, GetBankStatementImportStatisticsResponse>
{
    private readonly IBankStatementImportRepository _repository;

    public GetBankStatementImportStatisticsHandler(IBankStatementImportRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetBankStatementImportStatisticsResponse> Handle(GetBankStatementImportStatisticsRequest request, CancellationToken cancellationToken)
    {
        // If no date range specified, default to last 30 days
        var endDate = request.EndDate ?? DateTime.UtcNow.Date;
        var startDate = request.StartDate ?? endDate.AddDays(-29); // 30 days including today

        var statistics = await _repository.GetImportStatisticsAsync(startDate, endDate, request.DateType);

        // Create a complete range of dates to include days with zero imports
        var allDates = new List<BankStatementImportStatisticsDto>();
        var currentDate = startDate;
        
        while (currentDate <= endDate)
        {
            var statForDate = statistics.FirstOrDefault(s => s.Date.Date == currentDate.Date);
            
            allDates.Add(new BankStatementImportStatisticsDto
            {
                Date = currentDate,
                ImportCount = statForDate?.ImportCount ?? 0,
                TotalItemCount = statForDate?.TotalItemCount ?? 0
            });
            
            currentDate = currentDate.AddDays(1);
        }

        var result = new GetBankStatementImportStatisticsResponse
        {
            Statistics = allDates
        };

        return result;
    }
}