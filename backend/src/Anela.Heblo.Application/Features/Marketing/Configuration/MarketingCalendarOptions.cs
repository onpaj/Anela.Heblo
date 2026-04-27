namespace Anela.Heblo.Application.Features.Marketing.Configuration
{
    public class MarketingCalendarOptions
    {
        public const string SectionName = "MarketingCalendar";

        public string MailboxUpn { get; set; } = string.Empty;
        public bool PushEnabled { get; set; }
    }
}
