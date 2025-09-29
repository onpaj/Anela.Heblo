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
        var statistics = await _repository.GetImportStatisticsAsync(request.StartDate, request.EndDate);

        var result = new GetBankStatementImportStatisticsResponse
        {
            Statistics = statistics.Select(s => new BankStatementImportStatisticsDto
            {
                Date = s.Date,
                ImportCount = s.ImportCount,
                TotalItemCount = s.TotalItemCount
            })
        };

        return result;
    }
}