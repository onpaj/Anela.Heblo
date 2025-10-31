using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Application.Features.InvoiceClassification.Services;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

namespace Anela.Heblo.Application.Features.InvoiceClassification;

public static class InvoiceClassificationModule
{
    public static IServiceCollection AddInvoiceClassificationModule(this IServiceCollection services)
    {
        services.AddScoped<IInvoiceClassificationService, InvoiceClassificationService>();
        services.AddScoped<IRuleEvaluationEngine, RuleEvaluationEngine>();
        
        // Register all classification rule implementations
        services.AddScoped<IClassificationRule, VatClassificationRule>();
        services.AddScoped<IClassificationRule, CompanyNameClassificationRule>();
        services.AddScoped<IClassificationRule, DescriptionClassificationRule>();
        services.AddScoped<IClassificationRule, ItemDescriptionClassificationRule>();
        services.AddScoped<IClassificationRule, AmountClassificationRule>();
        
        return services;
    }
}