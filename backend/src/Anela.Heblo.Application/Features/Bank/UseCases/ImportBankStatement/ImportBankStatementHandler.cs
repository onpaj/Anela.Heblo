using System.Diagnostics;
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;

public class ImportBankStatementHandler : IRequestHandler<ImportBankStatementRequest, ImportBankStatementResponse>
{
    private readonly IBankClient _comgateClient;
    private readonly IBankStatementImportService _bankStatementImportService;
    private readonly IBankStatementImportRepository _repository;
    private readonly BankAccountSettings _bankSettings;
    private readonly IMapper _mapper;
    private readonly ILogger<ImportBankStatementHandler> _logger;

    public ImportBankStatementHandler(
        IBankClient comgateClient,
        IBankStatementImportService bankStatementImportService,
        IBankStatementImportRepository repository,
        IOptions<BankAccountSettings> bankSettings,
        IMapper mapper,
        ILogger<ImportBankStatementHandler> logger)
    {
        _comgateClient = comgateClient ?? throw new ArgumentNullException(nameof(comgateClient));
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
            "Bank import START - Account: {AccountName}, Date: {StatementDate}",
            request.AccountName, request.StatementDate);

        // Validate account configuration
        var accountSetting = _bankSettings.Accounts?.SingleOrDefault(a => a.Name == request.AccountName);
        if (accountSetting == null)
        {
            var availableAccounts = _bankSettings.Accounts != null
                ? string.Join(", ", _bankSettings.Accounts.Select(a => a.Name))
                : "None";

            _logger.LogError(
                "Bank import FAILED - Account not found: {AccountName}. Available accounts: {AvailableAccounts}",
                request.AccountName, availableAccounts);

            throw new ArgumentException($"Account name {request.AccountName} not found in {BankAccountSettings.ConfigurationKey} configuration. Available accounts: {availableAccounts}");
        }

        _logger.LogInformation(
            "Account config resolved - Account: {AccountName}, FlexiBeeId: {FlexiBeeId}, AccountNumber: {AccountNumber}",
            request.AccountName, accountSetting.FlexiBeeId, accountSetting.AccountNumber);

        // Get statements from Comgate
        var statements = await _comgateClient.GetStatementsAsync(accountSetting.AccountNumber, request.StatementDate);

        _logger.LogInformation(
            "Comgate returned {StatementCount} statements for processing - Account: {AccountName}",
            statements.Count, request.AccountName);

        var imports = new List<BankStatementImportDto>();

        foreach (var statement in statements)
        {
            try
            {
                _logger.LogInformation("Processing statement {StatementId}", statement.StatementId);

                // Get ABO data from Comgate
                var aboData = await _comgateClient.GetStatementAsync(statement.StatementId);

                // Create bank statement import entity
                var import = new BankStatementImport(statement.StatementId, request.StatementDate);

                // Import statement to accounting system
                var importResult = await _bankStatementImportService.ImportStatementAsync(accountSetting.FlexiBeeId, aboData.Data);

                // Set properties directly
                import.Account = accountSetting.AccountNumber;
                import.Currency = request.AccountName.EndsWith("EUR") ? CurrencyCode.EUR : CurrencyCode.CZK;
                import.ItemCount = aboData.ItemCount;
                import.ImportResult = importResult.IsSuccess ? ImportStatus.Success : importResult.ErrorMessage ?? ImportStatus.UnknownError;

                // Save to database
                var savedImport = await _repository.AddAsync(import);
                imports.Add(_mapper.Map<BankStatementImportDto>(savedImport));

                _logger.LogInformation("Successfully processed statement {StatementId} with result: {Result}",
                    statement.StatementId, import.ImportResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing statement {StatementId}", statement.StatementId);

                // Create failed import record
                var failedImport = new BankStatementImport(statement.StatementId, request.StatementDate);
                failedImport.Account = accountSetting.AccountNumber;
                failedImport.Currency = request.AccountName.EndsWith("EUR") ? CurrencyCode.EUR : CurrencyCode.CZK;
                failedImport.ImportResult = $"{ImportStatus.ProcessingError}: {ex.Message}";

                var savedFailedImport = await _repository.AddAsync(failedImport);
                imports.Add(_mapper.Map<BankStatementImportDto>(savedFailedImport));
            }
        }

        totalSw.Stop();

        var successCount = imports.Count(i => i.ImportResult == ImportStatus.Success);
        var errorCount = imports.Count - successCount;

        _logger.LogInformation(
            "Bank import COMPLETED - Account: {AccountName}, Total: {TotalCount}, Success: {SuccessCount}, Errors: {ErrorCount}, Duration: {Duration}ms",
            request.AccountName, imports.Count, successCount, errorCount, totalSw.ElapsedMilliseconds);

        return new ImportBankStatementResponse { Statements = imports };
    }
}