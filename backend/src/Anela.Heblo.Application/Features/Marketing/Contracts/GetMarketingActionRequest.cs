using System.Collections.Generic;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Marketing.Contracts
{
    public class GetMarketingActionRequest : IRequest<GetMarketingActionResponse>
    {
        public int Id { get; set; }
    }

    public class GetMarketingActionResponse : BaseResponse
    {
        public MarketingActionDto? Action { get; set; }

        public GetMarketingActionResponse() : base() { }

        public GetMarketingActionResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
            : base(errorCode, parameters) { }
    }
}
