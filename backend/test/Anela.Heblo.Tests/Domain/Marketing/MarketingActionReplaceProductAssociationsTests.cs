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
    }
}
