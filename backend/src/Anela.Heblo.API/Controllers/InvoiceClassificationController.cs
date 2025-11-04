using MediatR;
using Microsoft.AspNetCore.Mvc;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationRules;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.CreateClassificationRule;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.UpdateClassificationRule;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.DeleteClassificationRule;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ReorderClassificationRules;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifyInvoices;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetAccountingTemplates;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationHistory;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetInvoiceDetails;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InvoiceClassificationController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IEnumerable<IClassificationRule> _classificationRules;

    public InvoiceClassificationController(IMediator mediator, IEnumerable<IClassificationRule> classificationRules)
    {
        _mediator = mediator;
        _classificationRules = classificationRules;
    }

    [HttpGet("rules")]
    public async Task<ActionResult<GetClassificationRulesResponse>> GetRules([FromQuery] bool includeInactive = false)
    {
        var request = new GetClassificationRulesRequest { IncludeInactive = includeInactive };
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpPost("rules")]
    public async Task<ActionResult<CreateClassificationRuleResponse>> CreateRule([FromBody] CreateClassificationRuleRequest request)
    {
        var response = await _mediator.Send(request);
        return CreatedAtAction(nameof(GetRules), new { }, response);
    }

    [HttpPut("rules/reorder")]
    public async Task<ActionResult<ReorderClassificationRulesResponse>> ReorderRules([FromBody] ReorderClassificationRulesRequest request)
    {
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpPut("rules/{id}")]
    public async Task<ActionResult<UpdateClassificationRuleResponse>> UpdateRule(Guid id, [FromBody] UpdateClassificationRuleRequest request)
    {
        request.Id = id;
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpDelete("rules/{id}")]
    public async Task<ActionResult<DeleteClassificationRuleResponse>> DeleteRule(Guid id)
    {
        var request = new DeleteClassificationRuleRequest { Id = id };
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpPost("classify")]
    public async Task<ActionResult<ClassifyInvoicesResponse>> ClassifyInvoices([FromBody] ClassifyInvoicesRequest request)
    {
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpGet("rule-types")]
    public ActionResult<List<ClassificationRuleTypeDto>> GetAvailableRuleTypes()
    {
        var ruleTypes = _classificationRules.Select(rule => new ClassificationRuleTypeDto
        {
            Identifier = rule.Identifier,
            DisplayName = rule.DisplayName,
            Description = rule.Description
        }).ToList();

        return Ok(ruleTypes);
    }

    [HttpGet("accounting-templates")]
    public async Task<ActionResult<GetAccountingTemplatesResponse>> GetAccountingTemplates()
    {
        var request = new GetAccountingTemplatesRequest();
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpGet("history")]
    public async Task<ActionResult<GetClassificationHistoryResponse>> GetClassificationHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? invoiceNumber = null,
        [FromQuery] string? companyName = null)
    {
        var request = new GetClassificationHistoryRequest
        {
            Page = page,
            PageSize = pageSize,
            FromDate = fromDate,
            ToDate = toDate,
            InvoiceNumber = invoiceNumber,
            CompanyName = companyName
        };

        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpPost("classify/{invoiceId}")]
    public async Task<ActionResult<ClassifyInvoicesResponse>> ClassifySingleInvoice(string invoiceId)
    {
        var request = new ClassifyInvoicesRequest
        {
            InvoiceIds = new List<string> { invoiceId },
            ManualTrigger = true
        };
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpGet("invoice/{invoiceId}")]
    public async Task<ActionResult<GetInvoiceDetailsResponse>> GetInvoiceDetails(string invoiceId)
    {
        var request = new GetInvoiceDetailsRequest { InvoiceId = invoiceId };
        var response = await _mediator.Send(request);

        if (!response.Found)
        {
            return NotFound($"Invoice {invoiceId} not found");
        }

        return Ok(response);
    }
}