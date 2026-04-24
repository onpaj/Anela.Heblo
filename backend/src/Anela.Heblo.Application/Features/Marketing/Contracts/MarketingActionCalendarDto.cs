using System;
using System.Collections.Generic;

namespace Anela.Heblo.Application.Features.Marketing.Contracts
{
    public class MarketingActionCalendarDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = null!;
        public string ActionType { get; set; } = null!;
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public List<string> AssociatedProducts { get; set; } = new();
    }
}
