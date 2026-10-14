using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SubmitAdProposal;

/// <summary>No data-annotation attributes: the handler returns structured validation errors agents can act on.</summary>
public class SubmitAdProposalRequest : IRequest<SubmitAdProposalResponse>
{
    public AdPlatform Platform { get; set; }
    public string AccountExternalId { get; set; } = string.Empty;
    public AdActionType ActionType { get; set; }
    public List<AdActionInputDto> Actions { get; set; } = new();
    public string Reasoning { get; set; } = string.Empty;
    /// <summary>The caller's own Running agent run, if the proposal comes from an agent.</summary>
    public Guid? AgentRunId { get; set; }
}
