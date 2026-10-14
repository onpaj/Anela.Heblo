using Anela.Heblo.Application.Features.Packaging.UseCases.GetCarrierLabelOffsets;
using Anela.Heblo.Application.Features.Packaging.UseCases.SetCarrierLabelOffset;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Warehouse_Expedition)]
[ApiController]
[Route("api/carrier-label-offsets")]
public class CarrierLabelOffsetController : BaseApiController
{
    private readonly IMediator _mediator;

    public CarrierLabelOffsetController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [HttpGet]
    public async Task<ActionResult<GetCarrierLabelOffsetsResponse>> GetOffsets(
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetCarrierLabelOffsetsRequest(), cancellationToken);
        return HandleResponse(response);
    }

    [HttpPut]
    [FeatureAuthorize(Feature.Warehouse_Expedition, AccessLevel.Write)]
    public async Task<ActionResult<SetCarrierLabelOffsetResponse>> SetOffset(
        [FromBody] SetCarrierLabelOffsetRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }
}
