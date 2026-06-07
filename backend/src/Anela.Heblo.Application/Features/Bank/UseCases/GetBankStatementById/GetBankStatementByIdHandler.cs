using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Domain.Features.Bank;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementById;

public class GetBankStatementByIdHandler : IRequestHandler<GetBankStatementByIdRequest, BankStatementImportDto?>
{
    private readonly IBankStatementImportRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetBankStatementByIdHandler> _logger;

    public GetBankStatementByIdHandler(
        IBankStatementImportRepository repository,
        IMapper mapper,
        ILogger<GetBankStatementByIdHandler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<BankStatementImportDto?> Handle(GetBankStatementByIdRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting bank statement with ID {Id}", request.Id);

        var entity = await _repository.GetByIdAsync(request.Id);

        if (entity is null)
        {
            _logger.LogInformation("Bank statement with ID {Id} not found", request.Id);
            return null;
        }

        return _mapper.Map<BankStatementImportDto>(entity);
    }
}
