using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Flexi.Bank;

public class FlexiBankStatementImportService : IBankStatementImportService
{
    private readonly FlexiBankAccountClient _flexiBankAccountClient;
    private readonly ILogger<FlexiBankStatementImportService> _logger;

    public FlexiBankStatementImportService(
        FlexiBankAccountClient flexiBankAccountClient,
        ILogger<FlexiBankStatementImportService> logger)
    {
        _flexiBankAccountClient = flexiBankAccountClient ?? throw new ArgumentNullException(nameof(flexiBankAccountClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<bool>> ImportStatementAsync(int accountId, string statementData)
    {
        try
        {
            _logger.LogInformation("Importing bank statement to account {AccountId}", accountId);

            var flexiResult = await _flexiBankAccountClient.ImportStatementAsync(accountId, statementData);

            if (flexiResult.IsSuccess)
            {
                _logger.LogInformation("Bank statement successfully imported to account {AccountId}", accountId);
                return Result.Success(true);
            }
            else
            {
                _logger.LogWarning("Bank statement import failed for account {AccountId}: {ErrorMessage}", 
                    accountId, flexiResult.ErrorMessage);
                return Result.Failure<bool>(flexiResult.ErrorMessage ?? "Unknown import error");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while importing bank statement to account {AccountId}", accountId);
            return Result.Failure<bool>($"Exception during import: {ex.Message}");
        }
    }
}