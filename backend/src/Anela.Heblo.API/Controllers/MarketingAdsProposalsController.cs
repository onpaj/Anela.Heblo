using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ApproveAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdProposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RejectAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ReviseAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RevertAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SubmitAdProposal;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>Marketing → ad proposals: inbox, detail, submit/revise, version-bound approve/reject, revert.</summary>
[FeatureAuthorize(Feature.Marketing_Ads)]
[ApiController]
[Route("api/marketing-ads/proposals")]
public class MarketingAdsProposalsController : BaseApiController
{
    private readonly IMediator _mediator;

    public MarketingAdsProposalsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(ListAdProposalsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ListAdProposalsResponse>> ListProposals([FromQuery] ListAdProposalsRequest request) =>
        HandleResponse(await _mediator.Send(request));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(GetAdProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GetAdProposalResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<GetAdProposalResponse>> GetProposal(int id) =>
        HandleResponse(await _mediator.Send(new GetAdProposalRequest { Id = id }));

    [HttpPost]
    [FeatureAuthorize(Feature.Marketing_Ads, AccessLevel.Write)]
    [ProducesResponseType(typeof(SubmitAdProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SubmitAdProposalResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(SubmitAdProposalResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SubmitAdProposalResponse>> SubmitProposal([FromBody] SubmitAdProposalRequest request) =>
        HandleResponse(await _mediator.Send(request));

    [HttpPost("{id:int}/revisions")]
    [FeatureAuthorize(Feature.Marketing_Ads, AccessLevel.Write)]
    [ProducesResponseType(typeof(ReviseAdProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ReviseAdProposalResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ReviseAdProposalResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ReviseAdProposalResponse>> ReviseProposal(int id, [FromBody] ReviseAdProposalRequest request)
    {
        request.Id = id;
        return HandleResponse(await _mediator.Send(request));
    }

    [HttpPost("{id:int}/approve")]
    [FeatureAuthorize(Feature.Marketing_AdApprovals, AccessLevel.Write)]
    [ProducesResponseType(typeof(ApproveAdProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApproveAdProposalResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApproveAdProposalResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApproveAdProposalResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApproveAdProposalResponse>> ApproveProposal(int id, [FromBody] ApproveAdProposalRequest request)
    {
        request.Id = id;
        return HandleResponse(await _mediator.Send(request));
    }

    [HttpPost("{id:int}/reject")]
    [FeatureAuthorize(Feature.Marketing_AdApprovals, AccessLevel.Write)]
    [ProducesResponseType(typeof(RejectAdProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RejectAdProposalResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(RejectAdProposalResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RejectAdProposalResponse>> RejectProposal(int id, [FromBody] RejectAdProposalRequest request)
    {
        request.Id = id;
        return HandleResponse(await _mediator.Send(request));
    }

    [HttpPost("{id:int}/revert")]
    [FeatureAuthorize(Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    [ProducesResponseType(typeof(RevertAdProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RevertAdProposalResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(RevertAdProposalResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(RevertAdProposalResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RevertAdProposalResponse>> RevertProposal(int id) =>
        HandleResponse(await _mediator.Send(new RevertAdProposalRequest { Id = id }));
}
