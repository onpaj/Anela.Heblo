using System;
using System.Collections.Generic;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Application.Marketing;

public class MarketingActionDtoTests
{
    [Fact]
    public void FromEntity_ProjectsAllFields_ForFullyPopulatedAction()
    {
        // Arrange
        var createdAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var modifiedAt = new DateTime(2026, 2, 1, 11, 0, 0, DateTimeKind.Utc);
        var startDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        var action = new MarketingAction
        {
            Id = 42,
            Title = "Spring Campaign",
            Description = "Spring product launch",
            ActionType = MarketingActionType.Blog,
            StartDate = startDate,
            EndDate = endDate,
            CreatedAt = createdAt,
            ModifiedAt = modifiedAt,
            CreatedByUserId = "user-1",
            CreatedByUsername = "alice",
            ModifiedByUserId = "user-2",
            ModifiedByUsername = "bob",
            OutlookSyncStatus = MarketingSyncStatus.Synced,
            OutlookEventId = "outlook-event-99",
            ProductAssociations = new List<MarketingActionProduct>
            {
                new() { ProductCodePrefix = "PROD-A" },
                new() { ProductCodePrefix = "PROD-B" },
                new() { ProductCodePrefix = "PROD-A" }, // duplicate — must be de-duplicated
            },
            FolderLinks = new List<MarketingActionFolderLink>
            {
                new() { FolderKey = "folder-1", FolderType = MarketingFolderType.Seasonal },
                new() { FolderKey = "folder-2", FolderType = MarketingFolderType.Campaign },
            },
        };

        // Act
        var dto = MarketingActionDto.FromEntity(action);

        // Assert
        dto.Id.Should().Be(42);
        dto.Title.Should().Be("Spring Campaign");
        dto.Description.Should().Be("Spring product launch");
        dto.ActionType.Should().Be(MarketingActionType.Blog.ToString());
        dto.StartDate.Should().Be(startDate);
        dto.EndDate.Should().Be(endDate);
        dto.CreatedAt.Should().Be(createdAt);
        dto.ModifiedAt.Should().Be(modifiedAt);
        dto.CreatedByUserId.Should().Be("user-1");
        dto.CreatedByUsername.Should().Be("alice");
        dto.ModifiedByUserId.Should().Be("user-2");
        dto.ModifiedByUsername.Should().Be("bob");
        dto.OutlookSyncStatus.Should().Be(MarketingSyncStatus.Synced.ToString());
        dto.OutlookEventId.Should().Be("outlook-event-99");

        dto.AssociatedProducts.Should().Equal("PROD-A", "PROD-B");

        dto.FolderLinks.Should().HaveCount(2);
        dto.FolderLinks[0].FolderKey.Should().Be("folder-1");
        dto.FolderLinks[0].FolderType.Should().Be(MarketingFolderType.Seasonal.ToString());
        dto.FolderLinks[1].FolderKey.Should().Be("folder-2");
        dto.FolderLinks[1].FolderType.Should().Be(MarketingFolderType.Campaign.ToString());
    }
}
