using Anela.Heblo.Application.Features.Invoice.Services;
using Anela.Heblo.Domain.Features.Invoice;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Invoice;

/// <summary>
/// DI module for Invoice feature
/// </summary>
public static class InvoiceModule
{
    public static IServiceCollection AddInvoiceModule(this IServiceCollection services)
    {
        // Repository
        services.AddScoped<IIssuedInvoiceRepository, IssuedInvoiceRepository>();
        
        // Services
        services.AddScoped<IInvoiceImportService, InvoiceImportService>();
        
        // MediatR handlers are automatically registered by scanning the assembly
        
        return services;
    }
}