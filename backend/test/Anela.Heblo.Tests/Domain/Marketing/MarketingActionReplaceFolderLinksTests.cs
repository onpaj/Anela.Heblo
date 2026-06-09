using System;
using System.Collections.Generic;
using System.Linq;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionReplaceFolderLinksTests
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
        public void ReplaceFolderLinks_ClearsExisting_WhenInputIsEmpty()
        {
            // Arrange
            var action = CreateAction();
            action.LinkToFolder("key-1", MarketingFolderType.General);
            action.FolderLinks.Should().HaveCount(1);

            // Act
            action.ReplaceFolderLinks(
                Enumerable.Empty<(string folderKey, MarketingFolderType folderType)>(),
                UtcNow);

            // Assert
            action.FolderLinks.Should().BeEmpty();
        }
    }
}
