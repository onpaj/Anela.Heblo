using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Tests.Features.InvoiceClassification.TestHelpers;

internal static class InvoiceClassificationFixtures
{
    internal static ReceivedInvoice CreateInvoice(
        decimal totalAmount = 0m,
        string companyName = "",
        string description = "",
        params string[] itemNames)
    {
        return new ReceivedInvoice
        {
            CompanyName = companyName,
            Description = description,
            TotalAmount = totalAmount,
            Items = itemNames
                .Select(name => new ReceivedInvoiceItem { Name = name })
                .ToList()
        };
    }

    internal static ClassificationRule CreateRule(
        string ruleTypeIdentifier,
        string pattern,
        int order = 0,
        bool isActive = true)
    {
        var rule = new ClassificationRule(
            name: "test-rule",
            ruleTypeIdentifier: ruleTypeIdentifier,
            pattern: pattern,
            accountingTemplateCode: "TEMPLATE",
            department: null,
            createdBy: "test");

        rule.SetOrder(order);

        if (!isActive)
        {
            rule.Update(
                name: "test-rule",
                ruleTypeIdentifier: ruleTypeIdentifier,
                pattern: pattern,
                accountingTemplateCode: "TEMPLATE",
                department: null,
                isActive: false,
                updatedBy: "test");
        }

        return rule;
    }
}
