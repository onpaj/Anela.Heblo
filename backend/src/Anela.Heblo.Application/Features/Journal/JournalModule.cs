using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Persistence.Catalog.Journal;

namespace Anela.Heblo.Application.Features.Journal
{
    public static class JournalModule
    {
        public static IServiceCollection AddJournalModule(this IServiceCollection services)
        {
            // Register repositories
            services.AddScoped<IJournalRepository, JournalRepository>();
            services.AddScoped<IJournalTagRepository, JournalTagRepository>();

            // MediatR handlers are automatically registered by MediatR scan

            return services;
        }
    }
}