using Anela.Heblo.Application.Features.InvoiceClassification.Services;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification;

public class InvoiceClassificationServiceTests
{
    private readonly Mock<IClassificationRuleRepository> _ruleRepositoryMock = new();
    private readonly Mock<IClassificationHistoryRepository> _historyRepositoryMock = new();
    private readonly Mock<IInvoiceClassificationsClient> _classificationsClientMock = new();
    private readonly Mock<IRuleEvaluationEngine> _ruleEngineMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<ILogger<InvoiceClassificationService>> _loggerMock = new();
    private readonly InvoiceClassificationService _sut;

    public InvoiceClassificationServiceTests()
    {
        _sut = new InvoiceClassificationService(
            _ruleRepositoryMock.Object,
            _historyRepositoryMock.Object,
            _classificationsClientMock.Object,
            _ruleEngineMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ClassifyInvoiceAsync_NoMatchingRule_MarksForManualReviewAndRecordsHistory()
    {
        // Arrange
        var invoice = new ReceivedInvoice
        {
            InvoiceNumber = "INV-001",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice"
        };

        var currentUser = new CurrentUser("user-1", "test-user", "test@test.com", true);
        ClassificationHistory? capturedHistory = null;

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(currentUser);

        _ruleRepositoryMock
            .Setup(x => x.GetActiveRulesOrderedAsync())
            .ReturnsAsync(new List<ClassificationRule>());

        _ruleEngineMock
            .Setup(x => x.FindMatchingRule(invoice, It.IsAny<List<ClassificationRule>>()))
            .Returns((ClassificationRule?)null);

        _classificationsClientMock
            .Setup(x => x.MarkInvoiceForManualReviewAsync(
                invoice.InvoiceNumber,
                "No matching classification rule",
                It.IsAny<CancellationToken?>()))
            .ReturnsAsync(true);

        _historyRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<ClassificationHistory>()))
            .Callback<ClassificationHistory>(h => capturedHistory = h)
            .ReturnsAsync((ClassificationHistory h) => h);

        // Act
        var result = await _sut.ClassifyInvoiceAsync(invoice);

        // Assert
        result.Result.Should().Be(ClassificationResult.ManualReviewRequired);
        result.RuleId.Should().BeNull();
        result.AccountingTemplateCode.Should().BeNull();
        result.Department.Should().BeNull();
        result.ErrorMessage.Should().BeNull();

        capturedHistory.Should().NotBeNull();
        capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber);
        capturedHistory.InvoiceNumber.Should().Be(invoice.InvoiceNumber);
        capturedHistory.InvoiceDate.Should().Be(invoice.InvoiceDate);
        capturedHistory.CompanyName.Should().Be(invoice.CompanyName);
        capturedHistory.Description.Should().Be(invoice.Description);
        capturedHistory.Result.Should().Be(ClassificationResult.ManualReviewRequired);
        capturedHistory.ProcessedBy.Should().Be(currentUser.Name);
        capturedHistory.ClassificationRuleId.Should().BeNull();
        capturedHistory.AccountingTemplateCode.Should().BeNull();
        capturedHistory.Department.Should().BeNull();
        capturedHistory.ErrorMessage.Should().Be("No matching rule found");

        _classificationsClientMock.Verify(
            x => x.MarkInvoiceForManualReviewAsync(
                invoice.InvoiceNumber,
                "No matching classification rule",
                It.IsAny<CancellationToken?>()),
            Times.Once);

        _historyRepositoryMock.Verify(
            x => x.AddAsync(It.IsAny<ClassificationHistory>()),
            Times.Once);
    }

    [Fact]
    public async Task ClassifyInvoiceAsync_RuleMatchedAndAbraSucceeds_RecordsSuccessAndReturnsRuleResult()
    {
        // Arrange
        var invoice = new ReceivedInvoice
        {
            InvoiceNumber = "INV-002",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice with Rule"
        };

        var ruleWithId = new ClassificationRule(
            "Test Rule",
            "TYPE_A",
            "pattern",
            "TEMPLATE_001",
            "Sales",
            "admin-user");

        var currentUser = new CurrentUser("user-1", "test-user", "test@test.com", true);
        ClassificationHistory? capturedHistory = null;

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(currentUser);

        _ruleRepositoryMock
            .Setup(x => x.GetActiveRulesOrderedAsync())
            .ReturnsAsync(new List<ClassificationRule> { ruleWithId });

        _ruleEngineMock
            .Setup(x => x.FindMatchingRule(invoice, It.IsAny<List<ClassificationRule>>()))
            .Returns(ruleWithId);

        _classificationsClientMock
            .Setup(x => x.UpdateInvoiceClassificationAsync(
                invoice.InvoiceNumber,
                ruleWithId.AccountingTemplateCode,
                ruleWithId.Department,
                It.IsAny<CancellationToken?>()))
            .ReturnsAsync(true);

        _historyRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<ClassificationHistory>()))
            .Callback<ClassificationHistory>(h => capturedHistory = h)
            .ReturnsAsync((ClassificationHistory h) => h);

        // Act
        var result = await _sut.ClassifyInvoiceAsync(invoice);

        // Assert
        result.Result.Should().Be(ClassificationResult.Success);
        result.RuleId.Should().Be(ruleWithId.Id);
        result.AccountingTemplateCode.Should().Be("TEMPLATE_001");
        result.Department.Should().Be("Sales");
        result.ErrorMessage.Should().BeNull();

        capturedHistory.Should().NotBeNull();
        capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber);
        capturedHistory.InvoiceNumber.Should().Be(invoice.InvoiceNumber);
        capturedHistory.InvoiceDate.Should().Be(invoice.InvoiceDate);
        capturedHistory.CompanyName.Should().Be(invoice.CompanyName);
        capturedHistory.Description.Should().Be(invoice.Description);
        capturedHistory.Result.Should().Be(ClassificationResult.Success);
        capturedHistory.ProcessedBy.Should().Be(currentUser.Name);
        capturedHistory.ClassificationRuleId.Should().Be(ruleWithId.Id);
        capturedHistory.AccountingTemplateCode.Should().Be("TEMPLATE_001");
        capturedHistory.Department.Should().Be("Sales");
        capturedHistory.ErrorMessage.Should().BeNull();

        _classificationsClientMock.Verify(
            x => x.UpdateInvoiceClassificationAsync(
                invoice.InvoiceNumber,
                ruleWithId.AccountingTemplateCode,
                ruleWithId.Department,
                It.IsAny<CancellationToken?>()),
            Times.Once);

        _historyRepositoryMock.Verify(
            x => x.AddAsync(It.IsAny<ClassificationHistory>()),
            Times.Once);
    }

    [Fact]
    public async Task ClassifyInvoiceAsync_RuleMatchedAndAbraFails_RecordsErrorAndReturnsRuleIdForDisplay()
    {
        // Arrange
        var invoice = new ReceivedInvoice
        {
            InvoiceNumber = "INV-003",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice with ABRA Failure"
        };

        var matchedRule = new ClassificationRule(
            "Test Rule",
            "TYPE_B",
            "pattern",
            "TEMPLATE_002",
            "Purchases",
            "admin-user");

        var currentUser = new CurrentUser("user-2", "another-user", "another@test.com", true);
        ClassificationHistory? capturedHistory = null;

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(currentUser);

        _ruleRepositoryMock
            .Setup(x => x.GetActiveRulesOrderedAsync())
            .ReturnsAsync(new List<ClassificationRule> { matchedRule });

        _ruleEngineMock
            .Setup(x => x.FindMatchingRule(invoice, It.IsAny<List<ClassificationRule>>()))
            .Returns(matchedRule);

        _classificationsClientMock
            .Setup(x => x.UpdateInvoiceClassificationAsync(
                invoice.InvoiceNumber,
                matchedRule.AccountingTemplateCode,
                matchedRule.Department,
                It.IsAny<CancellationToken?>()))
            .ReturnsAsync(false);

        _historyRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<ClassificationHistory>()))
            .Callback<ClassificationHistory>(h => capturedHistory = h)
            .ReturnsAsync((ClassificationHistory h) => h);

        // Act
        var result = await _sut.ClassifyInvoiceAsync(invoice);

        // Assert
        result.Result.Should().Be(ClassificationResult.Error);
        result.RuleId.Should().Be(matchedRule.Id);
        result.Department.Should().Be("Purchases");
        result.AccountingTemplateCode.Should().BeNull();
        result.ErrorMessage.Should().Be("Failed to update invoice classification in ABRA");

        capturedHistory.Should().NotBeNull();
        capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber);
        capturedHistory.InvoiceNumber.Should().Be(invoice.InvoiceNumber);
        capturedHistory.InvoiceDate.Should().Be(invoice.InvoiceDate);
        capturedHistory.CompanyName.Should().Be(invoice.CompanyName);
        capturedHistory.Description.Should().Be(invoice.Description);
        capturedHistory.Result.Should().Be(ClassificationResult.Error);
        capturedHistory.ProcessedBy.Should().Be(currentUser.Name);
        capturedHistory.ClassificationRuleId.Should().Be(matchedRule.Id);
        capturedHistory.AccountingTemplateCode.Should().Be("TEMPLATE_002");
        capturedHistory.Department.Should().Be("Purchases");
        capturedHistory.ErrorMessage.Should().Be("Failed to update invoice classification in ABRA");

        _classificationsClientMock.Verify(
            x => x.UpdateInvoiceClassificationAsync(
                invoice.InvoiceNumber,
                matchedRule.AccountingTemplateCode,
                matchedRule.Department,
                It.IsAny<CancellationToken?>()),
            Times.Once);

        _historyRepositoryMock.Verify(
            x => x.AddAsync(It.IsAny<ClassificationHistory>()),
            Times.Once);
    }

    [Fact]
    public async Task ClassifyInvoiceAsync_ExceptionThrown_RecordsErrorWithMessageAndReturnsErrorResult()
    {
        // Arrange
        var invoice = new ReceivedInvoice
        {
            InvoiceNumber = "INV-004",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice with Exception"
        };

        var currentUser = new CurrentUser("user-3", "error-user", "error@test.com", true);
        ClassificationHistory? capturedHistory = null;

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(currentUser);

        _ruleRepositoryMock
            .Setup(x => x.GetActiveRulesOrderedAsync())
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        _historyRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<ClassificationHistory>()))
            .Callback<ClassificationHistory>(h => capturedHistory = h)
            .ReturnsAsync((ClassificationHistory h) => h);

        // Act
        var result = await _sut.ClassifyInvoiceAsync(invoice);

        // Assert
        result.Result.Should().Be(ClassificationResult.Error);
        result.RuleId.Should().BeNull();
        result.AccountingTemplateCode.Should().BeNull();
        result.Department.Should().BeNull();
        result.ErrorMessage.Should().Contain("Exception during classification")
            .And.Contain("Database connection failed");

        capturedHistory.Should().NotBeNull();
        capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber);
        capturedHistory.InvoiceNumber.Should().Be(invoice.InvoiceNumber);
        capturedHistory.InvoiceDate.Should().Be(invoice.InvoiceDate);
        capturedHistory.CompanyName.Should().Be(invoice.CompanyName);
        capturedHistory.Description.Should().Be(invoice.Description);
        capturedHistory.Result.Should().Be(ClassificationResult.Error);
        capturedHistory.ProcessedBy.Should().Be(currentUser.Name);
        capturedHistory.ClassificationRuleId.Should().BeNull();
        capturedHistory.AccountingTemplateCode.Should().BeNull();
        capturedHistory.Department.Should().BeNull();
        capturedHistory.ErrorMessage.Should().Contain("Exception during classification")
            .And.Contain("Database connection failed");

        _historyRepositoryMock.Verify(
            x => x.AddAsync(It.IsAny<ClassificationHistory>()),
            Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error classifying invoice")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
