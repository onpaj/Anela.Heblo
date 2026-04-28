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

            // Outlook calendar sync — use real Graph-backed service only when real Azure AD
            // authentication is active. Mock auth has no ITokenAcquisition registered, so DI
            // validation would fail; NoOpOutlookCalendarSync is used in those environments instead.
            var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
            var bypassJwt = configuration.GetValue<bool>("BypassJwtValidation", false);

            if (!useMockAuth && !bypassJwt)
            {
                // Graph HTTP client (safe to register multiple times — IHttpClientFactory deduplicates)
                services.AddHttpClient("MicrosoftGraph");
                services.AddScoped<IOutlookCalendarSync, OutlookCalendarSyncService>();
            }
            else
            {
                services.AddScoped<IOutlookCalendarSync, NoOpOutlookCalendarSync>();
            }

            services.AddHostedService<OutlookSyncRetryHostedService>();

            // MediatR handlers are auto-registered by assembly scan
            return services;
        }
    }
}
