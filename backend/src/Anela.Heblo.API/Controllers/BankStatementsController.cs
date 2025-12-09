using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/bank-statements")]
[Authorize]
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
    /// Import bank statements from Comgate for a specific account and date
    /// </summary>
    /// <param name="request">Import request containing account name and statement date</param>
    /// <returns>List of imported bank statements</returns>
    [HttpPost("import")]
    public async Task<ActionResult<BankStatementImportResultDto>> ImportStatements([FromBody] BankImportRequestDto request)
    {
        try
        {
            _logger.LogInformation("Importing bank statements for account {AccountName} on {StatementDate}", 
                request.AccountName, request.StatementDate);

            var importRequest = new ImportBankStatementRequest(request.AccountName, request.StatementDate);
            var response = await _mediator.Send(importRequest);

            var result = new BankStatementImportResultDto
            {
                Statements = response.Statements
            };

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid request for bank statement import");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while importing bank statements");
            return StatusCode(500, new { message = "An error occurred while importing bank statements" });
        }
    }

    /// <summary>
    /// Get list of bank statement imports with optional filtering and pagination
    /// </summary>
    /// <param name="id">Filter by import ID</param>
    /// <param name="statementDate">Filter by statement date</param>
    /// <param name="importDate">Filter by import date</param>
    /// <param name="skip">Number of records to skip (default: 0)</param>
    /// <param name="take">Number of records to take (default: 10, max: 100)</param>
    /// <param name="orderBy">Order by field (default: ImportDate)</param>
    /// <param name="ascending">Sort direction (default: false)</param>
    /// <returns>Paginated list of bank statement imports</returns>
    [HttpGet]
    public async Task<ActionResult<GetBankStatementListResponse>> GetBankStatements(
        [FromQuery] int? id = null,
        [FromQuery] string? statementDate = null,
        [FromQuery] string? importDate = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10,
        [FromQuery] string? orderBy = "ImportDate",
        [FromQuery] bool ascending = false)
    {
        try
        {
            _logger.LogInformation("Getting bank statements with Skip={Skip}, Take={Take}", skip, take);

            var request = new GetBankStatementListRequest
            {
                Id = id,
                StatementDate = statementDate,
                ImportDate = importDate,
                Skip = skip,
                Take = take,
                OrderBy = orderBy,
                Ascending = ascending
            };

            var response = await _mediator.Send(request);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while retrieving bank statements");
            return StatusCode(500, new { message = "An error occurred while retrieving bank statements" });
        }
    }

    /// <summary>
    /// Get a specific bank statement import by ID
    /// </summary>
    /// <param name="id">Bank statement import ID</param>
    /// <returns>Bank statement import details</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<BankStatementImportDto>> GetBankStatement(int id)
    {
        try
        {
            _logger.LogInformation("Getting bank statement with ID {Id}", id);

            var request = new GetBankStatementListRequest
            {
                Id = id,
                Take = 1
            };

            var response = await _mediator.Send(request);
            var statement = response.Items.FirstOrDefault();

            if (statement == null)
            {
                return NotFound(new { message = $"Bank statement import with ID {id} not found" });
            }

            return Ok(statement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while retrieving bank statement {Id}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving bank statement" });
        }
    }
}