using System;
using System.Linq;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionAssociateWithProductTests
    {
        private static readonly DateTime FixedUtcNow =
            new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        private static MarketingAction CreateAction() =>
            new MarketingActionTestBuilder()
                .WithTitle("Test Action")
                .WithStartDate(FixedUtcNow)
                .WithCreatedAt(FixedUtcNow)
                .WithModifiedAt(FixedUtcNow)
                .WithCreatedBy("user-1")
                .Build();

        [Fact]
        public void AssociateWithProduct_DeduplicatesAcrossCase_WhenCalledTwiceWithMixedCasing()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.AssociateWithProduct("ABC", FixedUtcNow);
            action.AssociateWithProduct("abc", FixedUtcNow);

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
            action.ProductAssociations.Single().ProductCodePrefix.Should().Be("ABC");
        }

        [Fact]
        public void AssociateWithProduct_NoOps_WhenLowercaseDuplicateOfExistingUppercase()
        {
            // Arrange
            var action = CreateAction();
            action.AssociateWithProduct("ABC", FixedUtcNow);

            // Act
            action.AssociateWithProduct("abc", FixedUtcNow);

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
        }

        [Fact]
        public void AssociateWithProduct_NoOps_WhenWhitespacePaddedLowercaseDuplicateOfExistingUppercase()
        {
            // Arrange
            var action = CreateAction();
            action.AssociateWithProduct("ABC", FixedUtcNow);

            // Act
            action.AssociateWithProduct(" abc ", FixedUtcNow);

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
        }

        [Fact]
        public void AssociateWithProduct_AddsNormalizedRow_WhenInputIsNewCode()
        {
            // Arrange
            var action = CreateAction();
            action.Id = 42;

            // Act
            action.AssociateWithProduct("xyz", FixedUtcNow);

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
            var added = action.ProductAssociations.Single();
            added.ProductCodePrefix.Should().Be("XYZ");
            added.MarketingActionId.Should().Be(42);
            added.CreatedAt.Should().Be(FixedUtcNow);
        }

        [Fact]
        public void AssociateWithProduct_NormalizesPaddedMixedCaseInput_WhenNoExistingAssociation()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.AssociateWithProduct("  aBc  ", FixedUtcNow);

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
            action.ProductAssociations.Single().ProductCodePrefix.Should().Be("ABC");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void AssociateWithProduct_Throws_WhenInputIsNullEmptyOrWhitespace(string? input)
        {
            // Arrange
            var action = CreateAction();

            // Act
            Action act = () => action.AssociateWithProduct(input!, FixedUtcNow);

            // Assert
            act.Should()
                .Throw<ArgumentException>()
                .Which.ParamName.Should().Be("productCode");
            action.ProductAssociations.Should().BeEmpty();
        }

        [Fact]
        public void AssociateWithProduct_AssignsCreatedAtFromUtcNowParameter_Exactly()
        {
            // Arrange
            var action = CreateAction();
            var fixedNow = new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

            // Act
            action.AssociateWithProduct("ABC", fixedNow);

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
            action.ProductAssociations.Single().CreatedAt.Should().Be(fixedNow);
        }
    }
}
