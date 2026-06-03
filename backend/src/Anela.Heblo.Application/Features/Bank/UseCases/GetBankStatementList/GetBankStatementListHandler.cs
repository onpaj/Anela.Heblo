using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Domain.Features.Bank;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;

public class GetBankStatementListHandler : IRequestHandler<GetBankStatementListRequest, GetBankStatementListResponse>
{
    private readonly IBankStatementImportRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetBankStatementListHandler> _logger;

    public GetBankStatementListHandler(
        IBankStatementImportRepository repository,
        IMapper mapper,
        ILogger<GetBankStatementListHandler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GetBankStatementListResponse> Handle(GetBankStatementListRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting bank statement list with Skip={Skip}, Take={Take}", request.Skip, request.Take);

        DateTime? statementDate = null;
        if (!string.IsNullOrEmpty(request.StatementDate) && DateTime.TryParse(request.StatementDate, out var parsedStatementDate))
        {
            statementDate = parsedStatementDate;
        }

        DateTime? importDate = null;
        if (!string.IsNullOrEmpty(request.ImportDate) && DateTime.TryParse(request.ImportDate, out var parsedImportDate))
        {
            importDate = parsedImportDate;
        }

        var filter = new BankStatementListFilter(
            Id: request.Id,
            StatementDate: statementDate,
            ImportDate: importDate);

        var (items, totalCount) = await _repository.GetFilteredAsync(
            filter,
            skip: request.Skip,
            take: request.Take,
            orderBy: request.OrderBy ?? "ImportDate",
            ascending: request.Ascending,
            cancellationToken: cancellationToken);

        var dtoList = _mapper.Map<List<BankStatementImportDto>>(items);

        _logger.LogInformation("Retrieved {Count} bank statements (total: {TotalCount})", dtoList.Count, totalCount);

        return new GetBankStatementListResponse
        {
            Items = dtoList,
            TotalCount = totalCount
        };
    }
}