using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Domain.Features.Bank;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;

public class GetBankAccountsHandler : IRequestHandler<GetBankAccountsRequest, GetBankAccountsResponse>
{
    private readonly BankAccountSettings _bankSettings;
    private readonly ILogger<GetBankAccountsHandler> _logger;

    public GetBankAccountsHandler(
        IOptions<BankAccountSettings> bankSettings,
        ILogger<GetBankAccountsHandler> logger)
    {
        _bankSettings = bankSettings?.Value ?? throw new ArgumentNullException(nameof(bankSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<GetBankAccountsResponse> Handle(GetBankAccountsRequest request, CancellationToken cancellationToken)
    {
        var accounts = (_bankSettings.Accounts ?? new List<BankAccountConfiguration>())
            .Select(a => new BankAccountDto
            {
                Name = a.Name,
                AccountNumber = a.AccountNumber,
                Provider = a.Provider.ToString(),
                Currency = a.Currency.ToString(),
            })
            .ToList();

        _logger.LogInformation("Retrieved {Count} bank accounts", accounts.Count);

        return Task.FromResult(new GetBankAccountsResponse { Accounts = accounts });
    }
}
