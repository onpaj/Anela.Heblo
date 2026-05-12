using System;

namespace Anela.Heblo.Domain.Features.Marketing
{
    public class MarketingActionQueryCriteria
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        public string? SearchTerm { get; set; }
        public MarketingActionType? ActionType { get; set; }

        public DateTime? StartDateFrom { get; set; }
        public DateTime? StartDateTo { get; set; }

        public DateTime? EndDateFrom { get; set; }
        public DateTime? EndDateTo { get; set; }

        public string? ProductCodePrefix { get; set; }

        public bool IncludeDeleted { get; set; } = false;
    }
}
