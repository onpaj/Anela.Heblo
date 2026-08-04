using Anela.Heblo.Application.Features.UserManagement.UseCases.GetDepartments;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[FeatureAuthorize(Feature.Finance_FinancialOverview, Feature.Purchase_InvoiceClassification)]
public class DepartmentsController : BaseApiController
{
    private readonly IMediator _mediator;

    public DepartmentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(GetDepartmentsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetDepartmentsResponse>> GetDepartments(CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetDepartmentsRequest(), cancellationToken);
        return HandleResponse(response);
    }
}