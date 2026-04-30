using Anela.Heblo.Domain.Features.Marketing;
using System;
using System.Collections.Generic;

namespace Anela.Heblo.Application.Features.Marketing.Configuration
{
    public sealed class MarketingCalendarOptions
    {
        public const string SectionName = "MarketingCalendar";

        public string GroupId { get; init; } = string.Empty;
        public bool PushEnabled { get; init; }

        public Dictionary<string, MarketingActionType> CategoryMappings { get; init; }
            = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<MarketingActionType, string> OutgoingCategories { get; init; }
            = new();
    }
}
