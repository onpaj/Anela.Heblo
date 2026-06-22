using System.Diagnostics;
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Features.Bank.Infrastructure;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;

public class ImportBankStatementHandler : IRequestHandler<ImportBankStatementRequest, ImportBankStatementResponse>
{
    private readonly IBankClientFactory _factory;
    private readonly IBankStatementImportService _bankStatementImportService;
    private readonly IBankStatementImportRepository _repository;
    private readonly BankAccountSettings _bankSettings;
    private readonly IMapper _mapper;
    private readonly ILogger<ImportBankStatementHandler> _logger;

    public ImportBankStatementHandler(
        IBankClientFactory factory,
        IBankStatementImportService bankStatementImportService,
        IBankStatementImportRepository repository,
        IOptions<BankAccountSettings> bankSettings,
        IMapper mapper,
        ILogger<ImportBankStatementHandler> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _bankStatementImportService = bankStatementImportService ?? throw new ArgumentNullException(nameof(bankStatementImportService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _bankSettings = bankSettings.Value ?? throw new ArgumentNullException(nameof(bankSettings));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ImportBankStatementResponse> Handle(ImportBankStatementRequest request, CancellationToken cancellationToken)
    {
        var totalSw = Stopwatch.StartNew();

        _logger.LogInformation(
            "Bank import START - Account: {AccountName}, DateFrom: {DateFrom}, DateTo: {DateTo}",
            request.AccountName, request.DateFrom, request.DateTo);

        var accountSetting = _bankSettings.Accounts?.SingleOrDefault(a => a.Name == request.AccountName);
        if (accountSetting == null)
        {
            var availableAccounts = _bankSettings.Accounts != null
                ? string.Join(", ", _bankSettings.Accounts.Select(a => a.Name))
                : "None";

            _logger.LogError(
                "Bank import FAILED - Account not found: {AccountName}. Available accounts: {AvailableAccounts}",
                request.AccountName, availableAccounts);

            throw new ArgumentException(
                $"Account name {request.AccountName} not found in {BankAccountSettings.ConfigurationKey} configuration. Available accounts: {availableAccounts}");
        }

        var client = _factory.GetClient(accountSetting);

        var statements = await client.GetStatementsAsync(accountSetting.AccountNumber, request.DateFrom, request.DateTo);

        _logger.LogInformation(
            "Bank client returned {StatementCount} statements - Account: {AccountName}",
            statements.Count, request.AccountName);

        var existingTransfers = await _repository.GetExistingTransfersAsync(
            accountSetting.Name, request.DateFrom, request.DateTo, cancellationToken);

        var imports = new List<BankStatementImportDto>();
        var skippedCount = 0;

        foreach (var statement in statements)
        {
            if (existingTransfers.TryGetValue(statement.StatementId, out var existingResult)
                && existingResult == ImportStatus.Success)
            {
                skippedCount++;
                _logger.LogDebug("Skipping already-imported statement {StatementId}", statement.StatementId);
                continue;
            }

            var isRetry = existingTransfers.ContainsKey(statement.StatementId);
            imports.Add(await ProcessStatementAsync(client, statement, accountSetting, isRetry, cancellationToken));
        }

        totalSw.Stop();

        var successCount = imports.Count(i => i.ImportResult == ImportStatus.Success);
        var errorCount = imports.Count - successCount;

        _logger.LogInformation(
            "Bank import COMPLETED - Account: {AccountName}, Attempted: {Attempted}, Success: {SuccessCount}, Errors: {ErrorCount}, Skipped: {SkippedCount}, Duration: {Duration}ms",
            request.AccountName, imports.Count, successCount, errorCount, skippedCount, totalSw.ElapsedMilliseconds);

        return new ImportBankStatementResponse
        {
            Statements = imports,
            SuccessCount = successCount,
            ErrorCount = errorCount,
            SkippedCount = skippedCount,
        };
    }

    private async Task<BankStatementImportDto> ProcessStatementAsync(
        IBankClient client,
        BankStatementHeader statement,
        BankAccountConfiguration accountSetting,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing statement {StatementId} (retry={IsRetry})", statement.StatementId, isRetry);

            var aboData = await client.GetStatementAsync(statement.StatementId);
            var importResult = await _bankStatementImportService.ImportStatementAsync(accountSetting.FlexiBeeId, aboData.Data);
            var resultStatus = importResult.IsSuccess
                ? ImportStatus.Success
                : importResult.ErrorMessage ?? ImportStatus.UnknownError;

            var saved = isRetry
                ? await UpsertExistingAsync(statement, accountSetting, aboData.ItemCount, resultStatus, cancellationToken)
                : await InsertNewAsync(statement, accountSetting, aboData.ItemCount, resultStatus);

            _logger.LogInformation("Processed statement {StatementId} with result: {Result}",
                statement.StatementId, resultStatus);
            return _mapper.Map<BankStatementImportDto>(saved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing statement {StatementId}", statement.StatementId);
            var errorStatus = $"{ImportStatus.ProcessingError}: {ex.Message}";
            var saved = isRetry
                ? await UpsertExistingAsync(statement, accountSetting, 0, errorStatus, cancellationToken)
                : await InsertNewAsync(statement, accountSetting, 0, errorStatus);
            return _mapper.Map<BankStatementImportDto>(saved);
        }
    }

    private async Task<BankStatementImport> InsertNewAsync(
        BankStatementHeader statement,
        BankAccountConfiguration accountSetting,
        int itemCount,
        string resultStatus)
    {
        var import = new BankStatementImport(statement.StatementId, statement.Date)
        {
            Account = accountSetting.Name,
            Currency = accountSetting.Currency,
            ItemCount = itemCount,
            ImportResult = resultStatus,
        };
        return await _repository.AddAsync(import);
    }

    private async Task<BankStatementImport> UpsertExistingAsync(
        BankStatementHeader statement,
        BankAccountConfiguration accountSetting,
        int itemCount,
        string resultStatus,
        CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByTransferIdAsync(statement.StatementId, cancellationToken);
        if (existing == null)
            return await InsertNewAsync(statement, accountSetting, itemCount, resultStatus);

        existing.Account = accountSetting.Name;
        existing.Currency = accountSetting.Currency;
        existing.UpdateImportOutcome(itemCount, resultStatus);
        return await _repository.UpdateAsync(existing);
    }
}
