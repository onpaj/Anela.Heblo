using System;
using System.Collections.Generic;
using System.Linq;
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
            services.AddOptions<MarketingCalendarOptions>()
                .Bind(configuration.GetSection(MarketingCalendarOptions.SectionName))
                .Validate(
                    o => !string.IsNullOrWhiteSpace(o.GroupId) || !o.PushEnabled,
                    "MarketingCalendar:GroupId must be configured when PushEnabled is true.")
                .Validate(o =>
                {
                    ValidateRoundTrip(o);
                    return true;
                }, "Marketing calendar round-trip validation failed.")
                .ValidateOnStart();

            services.AddScoped<IMarketingActionRepository, MarketingActionRepository>();

            // The mapper has no Graph dependencies — register in both auth modes.
            services.AddSingleton<IMarketingCategoryMapper, MarketingCategoryMapper>();

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

        /// <summary>
        /// Validates that every value in <see cref="MarketingCalendarOptions.OutgoingCategories"/>
        /// has a corresponding key in <see cref="MarketingCalendarOptions.CategoryMappings"/>
        /// (case-insensitive, trimmed). Throws <see cref="InvalidOperationException"/> listing
        /// all offending pairs when the round-trip check fails (FR-7).
        /// </summary>
        internal static void ValidateRoundTrip(MarketingCalendarOptions options)
        {
            if (options.OutgoingCategories.Count == 0)
            {
                return;
            }

            var keys = new HashSet<string>(
                (options.CategoryMappings?.Keys ?? Enumerable.Empty<string>()).Select(k => k.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var errors = new List<string>();

            foreach (var kv in options.OutgoingCategories)
            {
                var name = (kv.Value ?? string.Empty).Trim();

                if (string.IsNullOrEmpty(name) || !keys.Contains(name))
                {
                    errors.Add($"OutgoingCategories[{kv.Key}] = '{kv.Value}' has no matching key in CategoryMappings.");
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Marketing calendar configuration is invalid: " + string.Join("; ", errors));
            }
        }
    }
}
