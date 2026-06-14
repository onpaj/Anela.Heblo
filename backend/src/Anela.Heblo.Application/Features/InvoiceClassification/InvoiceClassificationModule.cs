using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Application.Features.InvoiceClassification.Services;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.InvoiceClassification.Rules;
using Anela.Heblo.Persistence.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification;

public static class InvoiceClassificationModule
{
    public static IServiceCollection AddInvoiceClassificationModule(this IServiceCollection services)
    {
        services.AddScoped<IInvoiceClassificationService, InvoiceClassificationService>();
        services.AddScoped<IRuleEvaluationEngine, RuleEvaluationEngine>();

        // Repositories (implementations live in the Persistence layer)
        services.AddScoped<IClassificationRuleRepository, ClassificationRuleRepository>();
        services.AddScoped<IClassificationHistoryRepository, ClassificationHistoryRepository>();

        // Register all classification rule implementations
        services.AddScoped<IClassificationRule, VatClassificationRule>();
        services.AddScoped<IClassificationRule, CompanyNameClassificationRule>();
        services.AddScoped<IClassificationRule, DescriptionClassificationRule>();
        services.AddScoped<IClassificationRule, ItemDescriptionClassificationRule>();
        services.AddScoped<IClassificationRule, AmountClassificationRule>();

        return services;
    }
}