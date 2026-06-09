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

        [Fact]
        public void ReplaceFolderLinks_ClearsExisting_WhenInputIsNull()
        {
            // Arrange
            var action = CreateAction();
            action.LinkToFolder("key-1", MarketingFolderType.General);

            // Act
            action.ReplaceFolderLinks(null, UtcNow);

            // Assert
            action.FolderLinks.Should().BeEmpty();
        }

        [Fact]
        public void ReplaceFolderLinks_TrimsWhitespaceFromFolderKey()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.ReplaceFolderLinks(
                new[] { ("  key-1  ", MarketingFolderType.General) },
                UtcNow);

            // Assert
            action.FolderLinks.Single().FolderKey.Should().Be("key-1");
        }

        [Fact]
        public void ReplaceFolderLinks_DeduplicatesByCompositeKey_WhenSameKeyAndType()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.ReplaceFolderLinks(
                new[]
                {
                    ("key-1", MarketingFolderType.General),
                    ("key-1", MarketingFolderType.General),
                    (" key-1 ", MarketingFolderType.General),
                },
                UtcNow);

            // Assert
            action.FolderLinks.Should().HaveCount(1);
        }

        [Fact]
        public void ReplaceFolderLinks_KeepsBothEntries_WhenSameKeyButDifferentType()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.ReplaceFolderLinks(
                new[]
                {
                    ("key-1", MarketingFolderType.General),
                    ("key-1", MarketingFolderType.Campaign),
                },
                UtcNow);

            // Assert
            action.FolderLinks.Should().HaveCount(2);
            action.FolderLinks.Select(f => f.FolderType)
                .Should().BeEquivalentTo(new[]
                {
                    MarketingFolderType.General,
                    MarketingFolderType.Campaign,
                });
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ReplaceFolderLinks_Throws_WhenAnyFolderKeyIsNullEmptyOrWhitespace(string? badKey)
        {
            // Arrange
            var action = CreateAction();

            // Act
            Action act = () =>
                action.ReplaceFolderLinks(
                    new[]
                    {
                        ("good", MarketingFolderType.General),
                        (badKey!, MarketingFolderType.General),
                    },
                    UtcNow);

            // Assert
            act.Should().Throw<ArgumentException>()
                .Which.ParamName.Should().Be("links");
        }
    }
}
