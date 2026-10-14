using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ReviseAdProposal;

public class ReviseAdProposalRequest : IRequest<ReviseAdProposalResponse>
{
    /// <summary>Set from the route by the controller.</summary>
    public int Id { get; set; }
    public int ExpectedVersion { get; set; }
    public List<AdActionInputDto> Actions { get; set; } = new();
    public string Reasoning { get; set; } = string.Empty;
}
