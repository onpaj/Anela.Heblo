using System;
using System.Collections.Generic;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Marketing.Contracts
{
    public class GetMarketingCalendarRequest : IRequest<GetMarketingCalendarResponse>
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class GetMarketingCalendarResponse : BaseResponse
    {
        public List<MarketingActionCalendarDto> Actions { get; set; } = new();
    }
}
