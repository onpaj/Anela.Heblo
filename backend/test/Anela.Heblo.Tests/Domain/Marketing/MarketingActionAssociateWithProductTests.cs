using System;
using System.Linq;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionAssociateWithProductTests
    {
        private static MarketingAction CreateAction()
        {
            return new MarketingAction
            {
                Title = "Test Action",
                StartDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                CreatedByUserId = "user-1",
            };
        }

        [Fact]
        public void AssociateWithProduct_DeduplicatesAcrossCase_WhenCalledTwiceWithMixedCasing()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.AssociateWithProduct("ABC");
            action.AssociateWithProduct("abc");

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
            action.ProductAssociations.Single().ProductCodePrefix.Should().Be("ABC");
        }

        [Fact]
        public void AssociateWithProduct_NoOps_WhenLowercaseDuplicateOfExistingUppercase()
        {
            // Arrange
            var action = CreateAction();
            action.AssociateWithProduct("ABC");

            // Act
            action.AssociateWithProduct("abc");

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
        }

        [Fact]
        public void AssociateWithProduct_NoOps_WhenWhitespacePaddedLowercaseDuplicateOfExistingUppercase()
        {
            // Arrange
            var action = CreateAction();
            action.AssociateWithProduct("ABC");

            // Act
            action.AssociateWithProduct(" abc ");

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
            action.AssociateWithProduct("xyz");

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
            var added = action.ProductAssociations.Single();
            added.ProductCodePrefix.Should().Be("XYZ");
            added.MarketingActionId.Should().Be(42);
            added.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void AssociateWithProduct_NormalizesPaddedMixedCaseInput_WhenNoExistingAssociation()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.AssociateWithProduct("  aBc  ");

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
            Action act = () => action.AssociateWithProduct(input!);

            // Assert
            act.Should()
                .Throw<ArgumentException>()
                .Which.ParamName.Should().Be("productCode");
            action.ProductAssociations.Should().BeEmpty();
        }
    }
}
