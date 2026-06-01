using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationRuleTypes;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification;

public class GetClassificationRuleTypesHandlerTests
{
    private static Mock<IClassificationRule> CreateRuleMock(string identifier, string displayName, string description)
    {
        var mock = new Mock<IClassificationRule>();
        mock.Setup(r => r.Identifier).Returns(identifier);
        mock.Setup(r => r.DisplayName).Returns(displayName);
        mock.Setup(r => r.Description).Returns(description);
        return mock;
    }

    [Fact]
    public async Task Handle_WithEmptyRuleCollection_ReturnsEmptyList()
    {
        // Arrange
        var handler = new GetClassificationRuleTypesHandler(Enumerable.Empty<IClassificationRule>());
        var request = new GetClassificationRuleTypesRequest();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.RuleTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithMultipleRules_ProjectsEachToDto()
    {
        // Arrange
        var rules = new[]
        {
            CreateRuleMock("rule-1", "Rule One", "Description one").Object,
            CreateRuleMock("rule-2", "Rule Two", "Description two").Object,
            CreateRuleMock("rule-3", "Rule Three", "Description three").Object
        };
        var handler = new GetClassificationRuleTypesHandler(rules);
        var request = new GetClassificationRuleTypesRequest();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.RuleTypes.Should().HaveCount(3);
        response.RuleTypes[0].Identifier.Should().Be("rule-1");
        response.RuleTypes[0].DisplayName.Should().Be("Rule One");
        response.RuleTypes[0].Description.Should().Be("Description one");
        response.RuleTypes[1].Identifier.Should().Be("rule-2");
        response.RuleTypes[2].Identifier.Should().Be("rule-3");
    }

    [Fact]
    public async Task Handle_PreservesEnumerationOrder()
    {
        // Arrange
        var rules = new[]
        {
            CreateRuleMock("alpha", "Alpha", "First").Object,
            CreateRuleMock("beta", "Beta", "Second").Object,
            CreateRuleMock("gamma", "Gamma", "Third").Object
        };
        var handler = new GetClassificationRuleTypesHandler(rules);
        var request = new GetClassificationRuleTypesRequest();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.RuleTypes.Select(r => r.Identifier).Should().ContainInOrder("alpha", "beta", "gamma");
    }
}
