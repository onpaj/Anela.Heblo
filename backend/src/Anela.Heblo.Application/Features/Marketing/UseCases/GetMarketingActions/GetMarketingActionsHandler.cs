using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Domain.Features.Marketing;
using MediatR;

namespace Anela.Heblo.Application.Features.Marketing.UseCases.GetMarketingActions
{
    public class GetMarketingActionsHandler : IRequestHandler<GetMarketingActionsRequest, GetMarketingActionsResponse>
    {
        private readonly IMarketingActionRepository _repository;

        public GetMarketingActionsHandler(IMarketingActionRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetMarketingActionsResponse> Handle(
            GetMarketingActionsRequest request,
            CancellationToken cancellationToken)
        {
            var criteria = new MarketingActionQueryCriteria
            {
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                SearchTerm = request.SearchTerm,
                ProductCodePrefix = request.ProductCodePrefix,
                StartDateFrom = request.StartDateFrom,
                StartDateTo = request.StartDateTo,
                EndDateFrom = request.EndDateFrom,
                EndDateTo = request.EndDateTo,
                IncludeDeleted = request.IncludeDeleted,
            };

            var result = await _repository.GetPagedAsync(criteria, cancellationToken);

            return new GetMarketingActionsResponse
            {
                Actions = result.Items.Select(MapToDto).ToList(),
                TotalCount = result.TotalCount,
                PageNumber = result.PageNumber,
                PageSize = result.PageSize,
                TotalPages = (int)Math.Ceiling((double)result.TotalCount / result.PageSize),
                HasNextPage = result.PageNumber * result.PageSize < result.TotalCount,
                HasPreviousPage = result.PageNumber > 1,
            };
        }

        internal static MarketingActionDto MapToDto(MarketingAction action) =>
            new()
            {
                Id = action.Id,
                Title = action.Title,
                Description = action.Description,
                ActionType = action.ActionType.ToString(),
                StartDate = action.StartDate,
                EndDate = action.EndDate,
                CreatedAt = action.CreatedAt,
                ModifiedAt = action.ModifiedAt,
                CreatedByUserId = action.CreatedByUserId,
                CreatedByUsername = action.CreatedByUsername,
                ModifiedByUserId = action.ModifiedByUserId,
                ModifiedByUsername = action.ModifiedByUsername,
                AssociatedProducts = action.ProductAssociations
                    .Select(pa => pa.ProductCodePrefix)
                    .Distinct()
                    .ToList(),
                FolderLinks = action.FolderLinks
                    .Select(fl => new MarketingActionFolderLinkDto
                    {
                        FolderKey = fl.FolderKey,
                        FolderType = fl.FolderType.ToString(),
                    })
                    .ToList(),
                OutlookSyncStatus = action.OutlookSyncStatus.ToString(),
                OutlookEventId = action.OutlookEventId,
            };
    }
}
