using System;
using System.Linq;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionLinkToFolderTests
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
        public void LinkToFolder_AssignsCreatedAtFromUtcNowParameter_Exactly()
        {
            // Arrange
            var action = CreateAction();
            var fixedNow = new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

            // Act
            action.LinkToFolder("inbox/marketing", MarketingFolderType.General, fixedNow);

            // Assert
            action.FolderLinks.Should().HaveCount(1);
            var link = action.FolderLinks.Single();
            link.CreatedAt.Should().Be(fixedNow);
            link.FolderKey.Should().Be("inbox/marketing");
            link.FolderType.Should().Be(MarketingFolderType.General);
        }

        [Fact]
        public void LinkToFolder_NoOps_WhenSameFolderKeyAlreadyLinked()
        {
            // Arrange
            var action = CreateAction();
            action.LinkToFolder("inbox/marketing", MarketingFolderType.General, FixedUtcNow);

            // Act
            action.LinkToFolder("inbox/marketing", MarketingFolderType.General, FixedUtcNow.AddHours(1));

            // Assert
            action.FolderLinks.Should().HaveCount(1);
            action.FolderLinks.Single().CreatedAt.Should().Be(FixedUtcNow);
        }

        [Fact]
        public void LinkToFolder_TrimsFolderKey()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.LinkToFolder("  inbox/marketing  ", MarketingFolderType.General, FixedUtcNow);

            // Assert
            action.FolderLinks.Single().FolderKey.Should().Be("inbox/marketing");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void LinkToFolder_Throws_WhenFolderKeyIsNullEmptyOrWhitespace(string? input)
        {
            // Arrange
            var action = CreateAction();

            // Act
            Action act = () => action.LinkToFolder(input!, MarketingFolderType.General, FixedUtcNow);

            // Assert
            act.Should().Throw<ArgumentException>()
                .Which.ParamName.Should().Be("folderKey");
            action.FolderLinks.Should().BeEmpty();
        }
    }
}
