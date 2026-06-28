using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementById;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Customer_BankStatements)]
[ApiController]
[Route("api/bank-statements")]
public class BankStatementsController : BaseApiController
{
    private readonly IMediator _mediator;
    private readonly ILogger<BankStatementsController> _logger;

    public BankStatementsController(IMediator mediator, ILogger<BankStatementsController> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get list of configured bank accounts available for import
    /// </summary>
    [HttpGet("accounts")]
    public async Task<ActionResult<IEnumerable<BankAccountDto>>> GetAccounts(CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetBankAccountsRequest(), cancellationToken);
        return Ok(response.Accounts);
    }

    /// <summary>
    /// Import bank statements from Comgate for a specific account and date
    /// </summary>
    /// <param name="request">Import request containing account name and statement date</param>
    /// <returns>List of imported bank statements</returns>
    [HttpPost("import")]
    public async Task<ActionResult<BankStatementImportResultDto>> ImportStatements([FromBody] BankImportRequestDto request)
    {
        _logger.LogInformation("Importing bank statements for account {AccountName} from {DateFrom} to {DateTo}",
            request.AccountName, request.DateFrom, request.DateTo);

        var importRequest = new ImportBankStatementRequest(request.AccountName, request.DateFrom, request.DateTo);
        var response = await _mediator.Send(importRequest);

        var result = new BankStatementImportResultDto
        {
            Statements = response.Statements
        };

        return Ok(result);
    }

    /// <summary>
    /// Get list of bank statement imports with optional filtering and pagination
    /// </summary>
    /// <param name="id">Filter by import ID</param>
    /// <param name="transferId">Case-insensitive substring filter on Transfer ID (max 100 chars)</param>
    /// <param name="account">Case-insensitive substring filter on Account name (max 100 chars)</param>
    /// <param name="statementDate">Filter by statement date (exact match)</param>
    /// <param name="importDate">Filter by import date (exact match)</param>
    /// <param name="dateFrom">Inclusive lower bound on statement date (ISO date)</param>
    /// <param name="dateTo">Inclusive upper bound on statement date (ISO date)</param>
    /// <param name="errorsOnly">When true, restricts to statements with ImportResult != "OK"</param>
    /// <param name="skip">Number of records to skip (default: 0)</param>
    /// <param name="take">Number of records to take (default: 10, max: 100)</param>
    /// <param name="orderBy">Order by field (default: ImportDate)</param>
    /// <param name="ascending">Sort direction (default: false)</param>
    /// <returns>Paginated list of bank statement imports</returns>
    [HttpGet]
    public async Task<ActionResult<GetBankStatementListResponse>> GetBankStatements(
        [FromQuery] int? id = null,
        [FromQuery] string? transferId = null,
        [FromQuery] string? account = null,
        [FromQuery] string? statementDate = null,
        [FromQuery] string? importDate = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null,
        [FromQuery] bool? errorsOnly = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10,
        [FromQuery] string? orderBy = "ImportDate",
        [FromQuery] bool ascending = false)
    {
        _logger.LogInformation("Getting bank statements with Skip={Skip}, Take={Take}", skip, take);

        var request = new GetBankStatementListRequest
        {
            Id = id,
            TransferId = transferId,
            Account = account,
            StatementDate = statementDate,
            ImportDate = importDate,
            DateFrom = dateFrom,
            DateTo = dateTo,
            ErrorsOnly = errorsOnly,
            Skip = skip,
            Take = take,
            OrderBy = orderBy,
            Ascending = ascending
        };

        var response = await _mediator.Send(request);
        return Ok(response);
    }

    /// <summary>
    /// Get a specific bank statement import by ID
    /// </summary>
    /// <param name="id">Bank statement import ID</param>
    /// <returns>Bank statement import details</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<BankStatementImportDto>> GetBankStatement(int id, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetBankStatementByIdRequest { Id = id }, cancellationToken);

        return response is null
            ? NotFound(new { message = $"Bank statement import with ID {id} not found" })
            : Ok(response);
    }
}