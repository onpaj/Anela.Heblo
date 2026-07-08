using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.InvoiceClassification;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification;

public class ClassificationRuleRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ClassificationRuleRepository _repository;

    public ClassificationRuleRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"ClassificationRuleTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new ClassificationRuleRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task GetMaxOrderAsync_WithNoRules_ReturnsZero()
    {
        // Act
        var maxOrder = await _repository.GetMaxOrderAsync();

        // Assert
        Assert.Equal(0, maxOrder);
    }

    [Fact]
    public async Task GetMaxOrderAsync_WithMultipleRules_ReturnsHighestOrder()
    {
        // Arrange
        var rule1 = new ClassificationRule(
            name: "Rule A",
            ruleTypeIdentifier: "CompanyName",
            pattern: "Acme",
            accountingTemplateCode: "TPL1",
            department: null,
            createdBy: "tester");
        rule1.SetOrder(1);

        var rule2 = new ClassificationRule(
            name: "Rule B",
            ruleTypeIdentifier: "CompanyName",
            pattern: "Globex",
            accountingTemplateCode: "TPL2",
            department: null,
            createdBy: "tester");
        rule2.SetOrder(5);

        var rule3 = new ClassificationRule(
            name: "Rule C",
            ruleTypeIdentifier: "CompanyName",
            pattern: "Initech",
            accountingTemplateCode: "TPL3",
            department: null,
            createdBy: "tester");
        rule3.SetOrder(3);

        _context.ClassificationRules.AddRange(rule1, rule2, rule3);
        await _context.SaveChangesAsync();

        // Act
        var maxOrder = await _repository.GetMaxOrderAsync();

        // Assert
        Assert.Equal(5, maxOrder);
    }
}
