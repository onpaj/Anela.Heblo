using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.UseCases.GetMarketingActions;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using MediatR;

namespace Anela.Heblo.Application.Features.Marketing.UseCases.GetMarketingAction
{
    public class GetMarketingActionHandler : IRequestHandler<GetMarketingActionRequest, GetMarketingActionResponse>
    {
        private readonly IMarketingActionRepository _repository;

        public GetMarketingActionHandler(IMarketingActionRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetMarketingActionResponse> Handle(
            GetMarketingActionRequest request,
            CancellationToken cancellationToken)
        {
            var action = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (action == null)
            {
                return new GetMarketingActionResponse(ErrorCodes.MarketingActionNotFound, new Dictionary<string, string>
                {
                    { "actionId", request.Id.ToString() },
                });
            }

            return new GetMarketingActionResponse
            {
                Action = GetMarketingActionsHandler.MapToDto(action),
            };
        }
    }
}
