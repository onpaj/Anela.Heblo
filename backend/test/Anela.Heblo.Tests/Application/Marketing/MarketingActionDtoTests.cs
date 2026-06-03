using System;
using System.Collections.Generic;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Tests.Domain.Marketing;
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

        var action = new MarketingActionTestBuilder()
            .WithId(42)
            .WithTitle("Spring Campaign")
            .WithDescription("Spring product launch")
            .WithActionType(MarketingActionType.Blog)
            .WithStartDate(startDate)
            .WithEndDate(endDate)
            .WithCreatedAt(createdAt)
            .WithModifiedAt(modifiedAt)
            .WithCreatedBy("user-1", "alice")
            .WithModifiedBy("user-2", "bob")
            .WithOutlookSyncStatus(MarketingSyncStatus.Synced)
            .WithOutlookEventId("outlook-event-99")
            .Build();
        action.ProductAssociations.Add(new MarketingActionProduct { ProductCodePrefix = "PROD-A" });
        action.ProductAssociations.Add(new MarketingActionProduct { ProductCodePrefix = "PROD-B" });
        action.ProductAssociations.Add(new MarketingActionProduct { ProductCodePrefix = "PROD-A" }); // duplicate — must be de-duplicated
        action.FolderLinks.Add(new MarketingActionFolderLink { FolderKey = "folder-1", FolderType = MarketingFolderType.Seasonal });
        action.FolderLinks.Add(new MarketingActionFolderLink { FolderKey = "folder-2", FolderType = MarketingFolderType.Campaign });

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
