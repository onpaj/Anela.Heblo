using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Domain.Features.Marketing;
using MediatR;

namespace Anela.Heblo.Application.Features.Marketing.UseCases.GetMarketingCalendar
{
    public class GetMarketingCalendarHandler : IRequestHandler<GetMarketingCalendarRequest, GetMarketingCalendarResponse>
    {
        private readonly IMarketingActionRepository _repository;

        public GetMarketingCalendarHandler(IMarketingActionRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetMarketingCalendarResponse> Handle(
            GetMarketingCalendarRequest request,
            CancellationToken cancellationToken)
        {
            var actions = await _repository.GetForCalendarAsync(
                request.StartDate,
                request.EndDate,
                cancellationToken);

            return new GetMarketingCalendarResponse
            {
                Actions = actions.Select(a => new MarketingActionCalendarDto
                {
                    Id = a.Id,
                    Title = a.Title,
                    ActionType = a.ActionType.ToString(),
                    StartDate = a.StartDate,
                    EndDate = a.EndDate,
                    AssociatedProducts = a.ProductAssociations
                        .Select(pa => pa.ProductCodePrefix)
                        .Distinct()
                        .ToList(),
                }).ToList(),
            };
        }
    }
}
