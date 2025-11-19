using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Persistence.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices;

/// <summary>
/// Module for registering Invoice-related services
/// </summary>
public static class InvoicesModule
{
    public static IServiceCollection AddInvoicesModule(this IServiceCollection services)
    {
        // Register repositories
        services.AddScoped<IIssuedInvoiceRepository, IssuedInvoiceRepository>();

        // MediatR handlers are automatically registered by MediatR scan

        return services;
    }
}