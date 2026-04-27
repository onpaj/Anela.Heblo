using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Persistence.Marketing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Marketing
{
    public static class MarketingModule
    {
        public static IServiceCollection AddMarketingModule(this IServiceCollection services, IConfiguration configuration)
        {
            // Bind options — fail startup when PushEnabled is true but MailboxUpn is missing
            services.AddOptions<MarketingCalendarOptions>()
                .Bind(configuration.GetSection(MarketingCalendarOptions.SectionName))
                .Validate(
                    o => !string.IsNullOrWhiteSpace(o.MailboxUpn) || !o.PushEnabled,
                    "MarketingCalendar:MailboxUpn must be configured when PushEnabled is true.")
                .ValidateOnStart();

            services.AddScoped<IMarketingActionRepository, MarketingActionRepository>();

            // Graph HTTP client (safe to register multiple times — IHttpClientFactory deduplicates)
            services.AddHttpClient("MicrosoftGraph");

            // Outlook calendar sync service
            services.AddScoped<IOutlookCalendarSync, OutlookCalendarSyncService>();

            // MediatR handlers are auto-registered by assembly scan
            return services;
        }
    }
}
