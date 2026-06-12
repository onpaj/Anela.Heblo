using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;
using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Bank;

namespace Anela.Heblo.Application.Features.Invoices;

/// <summary>
/// Module for registering Invoice-related services
/// </summary>
public static class InvoicesModule
{
    public static IServiceCollection AddInvoicesModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind ProductMappingOptions from configuration and validate at startup so
        // a missing or incomplete "ProductMapping" section fails fast instead of
        // silently registering a transformation with empty codes.
        services.AddOptions<ProductMappingOptions>()
            .Bind(configuration.GetSection(ProductMappingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register repositories
        services.AddScoped<IIssuedInvoiceRepository, IssuedInvoiceRepository>();

        // Cross-module contract: Invoices implements PackingMaterials' IInvoiceConsumptionSource
        // via an adapter. DI registration owned by provider (Invoices), not consumer
        // (PackingMaterials) — keeps the dependency direction inverted properly.
        services.AddScoped<IInvoiceConsumptionSource, InvoiceConsumptionSourceAdapter>();

        // Cross-module contract: Invoices implements Analytics' IInvoiceImportStatisticsSource
        // via an adapter. DI registration owned by provider (Invoices), not consumer
        // (Analytics) — mirrors the IInvoiceConsumptionSource pattern above. Scoped because
        // the adapter wraps ApplicationDbContext (also Scoped).
        services.AddScoped<IInvoiceImportStatisticsSource, InvoiceImportStatisticsSourceAdapter>();

        // Cross-module contracts: Invoices implements DataQuality's IInvoiceShoptetSource
        // and IInvoiceErpClient via adapters. Lifetimes mirror the wrapped services exactly:
        //   - IIssuedInvoiceSource is registered Singleton in Program.cs:119, so the adapter
        //     must also be Singleton (and DataQuality consumers must resolve it from a Scoped
        //     scope as usual — Singleton from Scoped is legal, the inverse is captive).
        //   - IIssuedInvoiceClient is registered Scoped in FlexiAdapterServiceCollectionExtensions.cs:93,
        //     so the adapter must also be Scoped.
        services.AddSingleton<IInvoiceShoptetSource, InvoiceShoptetSourceAdapter>();
        services.AddScoped<IInvoiceErpClient, InvoiceErpClientAdapter>();

        // Register services
        services.AddScoped<IInvoiceImportService, InvoiceImportService>();

        // Hangfire jobs are now automatically discovered via IRecurringJob interface

        // Register FlexiBee client (from SDK)
        // Note: IIssuedInvoiceClient registration should be done in Flexi adapter module

        // Register transformations — preserve registration order; the import pipeline
        // enumerates IEnumerable<IIssuedInvoiceImportTransformation> in this order.
        services.AddTransient<IIssuedInvoiceImportTransformation, GiftWithoutVATIssuedInvoiceImportTransformation>();
        services.AddTransient<IIssuedInvoiceImportTransformation, RemoveDAtTheEndOfProductCodeIssuedInvoiceImportTransformation>();
        services.AddTransient<IIssuedInvoiceImportTransformation>(provider =>
        {
            var opts = provider.GetRequiredService<IOptions<ProductMappingOptions>>().Value;
            return new ProductMappingIssuedInvoiceImportTransformation(opts.ShoptetCode, opts.ErpCode);
        });

        // MediatR handlers are automatically registered by MediatR scan

        return services;
    }
}
