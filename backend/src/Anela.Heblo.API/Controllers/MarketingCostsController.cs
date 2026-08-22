using Anela.Heblo.API.Infrastructure;
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostDetail;
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostsList;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/marketing-costs")]
public class MarketingCostsController : BaseApiController
{
    private readonly IMediator _mediator;

    public MarketingCostsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetMarketingCostsListResponse>> GetList([FromQuery] GetMarketingCostsListRequest request)
    {
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GetMarketingCostDetailResponse>> GetDetail(int id)
    {
        var request = new GetMarketingCostDetailRequest { Id = id };
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }
}
