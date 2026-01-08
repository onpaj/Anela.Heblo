using Anela.Heblo.Domain.Shared;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.BankAccounts;

namespace Anela.Heblo.Adapters.Flexi.Bank;

public class FlexiBankAccountClient
{
    private readonly IBankAccountClient _client;
    private readonly ILogger<FlexiBankAccountClient> _logger;

    public FlexiBankAccountClient(
        IBankAccountClient client,
        ILogger<FlexiBankAccountClient> logger
        )
    {
        _client = client;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<bool>> ImportStatementAsync(int accountId, string aboData)
    {
        try
        {
            _logger.LogInformation("Importing bank statement to FlexiBee account {AccountId}", accountId);

            var result = await _client.ImportStatement(accountId, aboData);
            return result.IsSuccess ? Result.Success(true) : Result.Failure<bool>(result.GetErrorMessage());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while importing bank statement to FlexiBee account {AccountId}", accountId);
            return Result.Failure<bool>($"Exception during FlexiBee import: {ex.Message}");
        }
    }
}