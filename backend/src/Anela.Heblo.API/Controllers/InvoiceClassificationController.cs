using MediatR;
using Microsoft.AspNetCore.Mvc;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationRules;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.CreateClassificationRule;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.UpdateClassificationRule;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.DeleteClassificationRule;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ReorderClassificationRules;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifyInvoices;
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
}