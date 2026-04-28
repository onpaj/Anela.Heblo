namespace Anela.Heblo.Application.Features.Marketing.Configuration
{
    public sealed class MarketingCalendarOptions
    {
        public const string SectionName = "MarketingCalendar";

        public string GroupId { get; init; } = string.Empty;
        public bool PushEnabled { get; init; }
    }
}
