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
                ActionType = request.ActionType,
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
                Actions = result.Items.Select(MarketingActionDto.FromEntity).ToList(),
                TotalCount = result.TotalCount,
                PageNumber = result.PageNumber,
                PageSize = result.PageSize,
                TotalPages = (int)Math.Ceiling((double)result.TotalCount / result.PageSize),
                HasNextPage = result.PageNumber * result.PageSize < result.TotalCount,
                HasPreviousPage = result.PageNumber > 1,
            };
        }
    }
}
