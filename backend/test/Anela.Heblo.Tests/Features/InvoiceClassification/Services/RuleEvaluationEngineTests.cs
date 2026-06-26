using Anela.Heblo.Application.Features.InvoiceClassification.Services;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Tests.Features.InvoiceClassification.TestHelpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification.Services;

public class RuleEvaluationEngineTests
{
    private static Mock<IClassificationRule> CreateStrategyMock(string identifier, bool evaluateResult)
    {
        var mock = new Mock<IClassificationRule>();
        mock.Setup(r => r.Identifier).Returns(identifier);
        mock.Setup(r => r.Evaluate(It.IsAny<ReceivedInvoice>(), It.IsAny<string>())).Returns(evaluateResult);
        return mock;
    }

    [Fact]
    public void FindMatchingRule_MultipleMatchingRules_ReturnsLowestOrderMatch()
    {
        // Arrange
        var strategyA = CreateStrategyMock("RULE_A", evaluateResult: true);
        var strategyB = CreateStrategyMock("RULE_B", evaluateResult: true);

        var sut = new RuleEvaluationEngine(new[] { strategyA.Object, strategyB.Object });

        var invoice = InvoiceClassificationFixtures.CreateInvoice();
        var ruleHigherOrder = InvoiceClassificationFixtures.CreateRule("RULE_B", pattern: "p", order: 2);
        var ruleLowerOrder = InvoiceClassificationFixtures.CreateRule("RULE_A", pattern: "p", order: 1);

        var rules = new List<ClassificationRule> { ruleHigherOrder, ruleLowerOrder };

        // Act
        var match = sut.FindMatchingRule(invoice, rules);

        // Assert
        match.Should().BeSameAs(ruleLowerOrder);
    }

    [Fact]
    public void FindMatchingRule_SkipsInactiveRules()
    {
        // Arrange
        var strategy = CreateStrategyMock("RULE_A", evaluateResult: true);
        var sut = new RuleEvaluationEngine(new[] { strategy.Object });

        var invoice = InvoiceClassificationFixtures.CreateInvoice();
        var inactiveRule = InvoiceClassificationFixtures.CreateRule("RULE_A", pattern: "p", order: 1, isActive: false);
        var activeRule = InvoiceClassificationFixtures.CreateRule("RULE_A", pattern: "p", order: 2, isActive: true);

        var rules = new List<ClassificationRule> { inactiveRule, activeRule };

        // Act
        var match = sut.FindMatchingRule(invoice, rules);

        // Assert
        match.Should().BeSameAs(activeRule);
    }

    [Fact]
    public void FindMatchingRule_NoActiveRuleMatches_ReturnsNull()
    {
        // Arrange
        var strategy = CreateStrategyMock("RULE_A", evaluateResult: false);
        var sut = new RuleEvaluationEngine(new[] { strategy.Object });

        var invoice = InvoiceClassificationFixtures.CreateInvoice();
        var rule = InvoiceClassificationFixtures.CreateRule("RULE_A", pattern: "p", order: 1);

        var rules = new List<ClassificationRule> { rule };

        // Act
        var match = sut.FindMatchingRule(invoice, rules);

        // Assert
        match.Should().BeNull();
    }

    [Fact]
    public void FindMatchingRule_EmptyRulesList_ReturnsNull()
    {
        // Arrange
        var sut = new RuleEvaluationEngine(Enumerable.Empty<IClassificationRule>());

        var invoice = InvoiceClassificationFixtures.CreateInvoice();

        // Act
        var match = sut.FindMatchingRule(invoice, new List<ClassificationRule>());

        // Assert
        match.Should().BeNull();
    }

    [Fact]
    public void FindMatchingRule_UnknownRuleTypeIdentifier_DoesNotThrowAndReturnsNull()
    {
        // Arrange
        var strategy = CreateStrategyMock("RULE_A", evaluateResult: true);
        var sut = new RuleEvaluationEngine(new[] { strategy.Object });

        var invoice = InvoiceClassificationFixtures.CreateInvoice();
        var unknownIdentifierRule = InvoiceClassificationFixtures.CreateRule("UNKNOWN_RULE", pattern: "p", order: 1);

        var rules = new List<ClassificationRule> { unknownIdentifierRule };

        // Act
        var act = () => sut.FindMatchingRule(invoice, rules);

        // Assert
        act.Should().NotThrow();
        act().Should().BeNull();
    }

    [Fact]
    public void FindMatchingRule_SortsByOrder_NotByListInsertionOrder()
    {
        // Arrange
        var strategy = CreateStrategyMock("RULE_A", evaluateResult: true);
        var sut = new RuleEvaluationEngine(new[] { strategy.Object });

        var invoice = InvoiceClassificationFixtures.CreateInvoice();
        var insertedFirstHighOrder = InvoiceClassificationFixtures.CreateRule("RULE_A", pattern: "p", order: 10);
        var insertedSecondLowOrder = InvoiceClassificationFixtures.CreateRule("RULE_A", pattern: "p", order: 1);

        // List insertion order intentionally reversed vs. desired evaluation order.
        var rules = new List<ClassificationRule> { insertedFirstHighOrder, insertedSecondLowOrder };

        // Act
        var match = sut.FindMatchingRule(invoice, rules);

        // Assert
        match.Should().BeSameAs(insertedSecondLowOrder);
    }

    [Fact]
    public void FindMatchingRule_FirstMatch_ShortCircuitsSubsequentEvaluations()
    {
        // Arrange — distinct Order values (1, 2, 3) make the iteration sequence unambiguous.
        // Strategies are separate mocks so we can Verify call counts independently.
        var firstStrategy = CreateStrategyMock("RULE_1", evaluateResult: true);
        var laterStrategyA = CreateStrategyMock("RULE_2", evaluateResult: true);
        var laterStrategyB = CreateStrategyMock("RULE_3", evaluateResult: true);

        var sut = new RuleEvaluationEngine(new[]
        {
            firstStrategy.Object,
            laterStrategyA.Object,
            laterStrategyB.Object
        });

        var invoice = InvoiceClassificationFixtures.CreateInvoice();
        var firstRule = InvoiceClassificationFixtures.CreateRule("RULE_1", pattern: "p", order: 1);
        var secondRule = InvoiceClassificationFixtures.CreateRule("RULE_2", pattern: "p", order: 2);
        var thirdRule = InvoiceClassificationFixtures.CreateRule("RULE_3", pattern: "p", order: 3);

        var rules = new List<ClassificationRule> { firstRule, secondRule, thirdRule };

        // Act
        var match = sut.FindMatchingRule(invoice, rules);

        // Assert
        match.Should().BeSameAs(firstRule);
        firstStrategy.Verify(s => s.Evaluate(invoice, It.IsAny<string>()), Times.Once);
        laterStrategyA.Verify(s => s.Evaluate(It.IsAny<ReceivedInvoice>(), It.IsAny<string>()), Times.Never);
        laterStrategyB.Verify(s => s.Evaluate(It.IsAny<ReceivedInvoice>(), It.IsAny<string>()), Times.Never);
    }
}
