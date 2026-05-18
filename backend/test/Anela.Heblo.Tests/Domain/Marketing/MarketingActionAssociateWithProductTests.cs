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
    }
}
