using System;
using System.Collections.Generic;
using System.Linq;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionReplaceProductAssociationsTests
    {
        private static readonly DateTime UtcNow = new(2026, 6, 9, 12, 0, 0, DateTimeKind.Utc);

        private static MarketingAction CreateAction() =>
            new MarketingActionTestBuilder()
                .WithId(1)
                .WithTitle("Test Action")
                .WithStartDate(UtcNow)
                .WithCreatedAt(UtcNow)
                .WithModifiedAt(UtcNow)
                .WithCreatedBy("user-1")
                .Build();

        [Fact]
        public void ReplaceProductAssociations_ClearsExisting_WhenInputIsEmpty()
        {
            // Arrange
            var action = CreateAction();
            action.AssociateWithProduct("ABC");
            action.AssociateWithProduct("XYZ");
            action.ProductAssociations.Should().HaveCount(2);

            // Act
            action.ReplaceProductAssociations(Enumerable.Empty<string>(), UtcNow);

            // Assert
            action.ProductAssociations.Should().BeEmpty();
        }

        [Fact]
        public void ReplaceProductAssociations_ClearsExisting_WhenInputIsNull()
        {
            // Arrange
            var action = CreateAction();
            action.AssociateWithProduct("ABC");

            // Act
            action.ReplaceProductAssociations(null, UtcNow);

            // Assert
            action.ProductAssociations.Should().BeEmpty();
        }

        [Fact]
        public void ReplaceProductAssociations_NormalizesAndDeduplicates_AcrossCaseAndWhitespace()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.ReplaceProductAssociations(new[] { "abc", "ABC", " abc " }, UtcNow);

            // Assert
            action.ProductAssociations.Should().HaveCount(1);
            action.ProductAssociations.Single().ProductCodePrefix.Should().Be("ABC");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ReplaceProductAssociations_Throws_WhenSequenceContainsNullEmptyOrWhitespace(string? badEntry)
        {
            // Arrange
            var action = CreateAction();

            // Act
            Action act = () =>
                action.ReplaceProductAssociations(new[] { "GOOD", badEntry! }, UtcNow);

            // Assert
            act.Should().Throw<ArgumentException>()
                .Which.ParamName.Should().Be("productCodes");
        }
    }
}
