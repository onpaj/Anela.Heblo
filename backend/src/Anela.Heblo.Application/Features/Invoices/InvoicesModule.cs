using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Persistence.Features.Invoices;
using Anela.Heblo.Application.Features.Invoices.Infrastructure.Mocks;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;
using Rem.FlexiBeeSDK.Client;

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

        // Register FlexiBee client (from SDK)
        // Note: IIssuedInvoiceClient registration should be done in Flexi adapter module
        
        // Register mock services (out of scope implementations)
        services.AddScoped<IBankClient, MockBankClient>();

        // Register transformations
        services.AddTransient<IIssuedInvoiceImportTransformation, GiftWithoutVATIssuedInvoiceImportTransformation>();
        services.AddTransient<IIssuedInvoiceImportTransformation, RemoveDAtTheEndOfProductCodeIssuedInvoiceImportTransformation>();
        
        // Product mapping transformations can be registered based on configuration
        // services.AddTransient<IIssuedInvoiceImportTransformation>(provider => 
        //     new ProductMappingIssuedInvoiceImportTransformation("OLD_CODE", "NEW_CODE"));

        // MediatR handlers are automatically registered by MediatR scan

        return services;
    }
}