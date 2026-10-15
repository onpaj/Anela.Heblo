using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdProposals;

public class ListAdProposalsHandler : IRequestHandler<ListAdProposalsRequest, ListAdProposalsResponse>
{
    private readonly IAdProposalRepository _proposals;
    private readonly IAdsReadModel _readModel;
    private readonly ICurrentUserService _currentUser;

    public ListAdProposalsHandler(IAdProposalRepository proposals, IAdsReadModel readModel, ICurrentUserService currentUser)
    {
        _proposals = proposals;
        _readModel = readModel;
        _currentUser = currentUser;
    }

    public async Task<ListAdProposalsResponse> Handle(ListAdProposalsRequest request, CancellationToken cancellationToken)
    {
        string? creator = null;
        if (request.Mine)
        {
            creator = _currentUser.GetCurrentUser().Id;
            if (string.IsNullOrWhiteSpace(creator)) return new ListAdProposalsResponse(ErrorCodes.Unauthorized);
        }

        var (items, total) = await _proposals.ListAsync(new AdProposalQuery
        {
            Status = request.Status,
            Platform = request.Platform?.ToString(),
            CreatedByPrincipal = creator,
            OldestFirst = request.OldestFirst,
            Page = request.Page,
            PageSize = request.PageSize,
        }, cancellationToken);

        // One name lookup per (platform, account) page group; the repository's order is kept.
        var namesByAccount = new Dictionary<(string Platform, string Account), IReadOnlyDictionary<string, string>>();
        foreach (var group in items.GroupBy(p => (p.Platform, Account: p.AccountExternalId)))
        {
            namesByAccount[group.Key] = await _readModel.GetEntityNamesAsync(Enum.Parse<AdPlatform>(group.Key.Platform), group.Key.Account,
                group.SelectMany(AdProposalDtoMapper.TargetIds).Distinct(StringComparer.Ordinal).ToList(), cancellationToken);
        }

        return new ListAdProposalsResponse
        {
            Items = items.Select(p => AdProposalDtoMapper.ToSummary(p, namesByAccount[(p.Platform, p.AccountExternalId)])).ToList(),
            TotalCount = total,
        };
    }
}
