using Anela.Heblo.Application.Features.Journal.Mapping;
using Anela.Heblo.Domain.Features.Journal;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class JournalEntryMapperTests
{
    private static JournalEntry BuildFullEntry()
    {
        var tag1 = new JournalEntryTag { Id = 10, Name = "Important", Color = "#FF0000" };
        var tag2 = new JournalEntryTag { Id = 20, Name = "Follow-up", Color = "#00FF00" };

        var entry = new JournalEntry
        {
            Id = 42,
            Title = "Test Entry",
            Content = "Test content body",
            EntryDate = new DateTime(2025, 1, 15),
            CreatedAt = new DateTime(2025, 1, 15, 10, 0, 0),
            ModifiedAt = new DateTime(2025, 1, 15, 11, 0, 0),
            CreatedByUserId = "user-001",
            CreatedByUsername = "alice",
            ModifiedByUserId = "user-002",
            ModifiedByUsername = "bob"
        };

        entry.ProductAssociations.Add(new JournalEntryProduct { ProductCodePrefix = "TON001", JournalEntryId = 42 });
        entry.ProductAssociations.Add(new JournalEntryProduct { ProductCodePrefix = "AKL002", JournalEntryId = 42 });

        entry.TagAssignments.Add(new JournalEntryTagAssignment { TagId = tag1.Id, Tag = tag1 });
        entry.TagAssignments.Add(new JournalEntryTagAssignment { TagId = tag2.Id, Tag = tag2 });

        return entry;
    }

    [Fact]
    public void ToDto_MapsAllScalarFields()
    {
        // Arrange
        var entry = BuildFullEntry();

        // Act
        var dto = JournalEntryMapper.ToDto(entry);

        // Assert
        dto.Id.Should().Be(42);
        dto.Title.Should().Be("Test Entry");
        dto.Content.Should().Be("Test content body");
        dto.EntryDate.Should().Be(new DateTime(2025, 1, 15));
        dto.CreatedAt.Should().Be(new DateTime(2025, 1, 15, 10, 0, 0));
        dto.ModifiedAt.Should().Be(new DateTime(2025, 1, 15, 11, 0, 0));
        dto.CreatedByUserId.Should().Be("user-001");
        dto.CreatedByUsername.Should().Be("alice");
        dto.ModifiedByUserId.Should().Be("user-002");
        dto.ModifiedByUsername.Should().Be("bob");
    }

    [Fact]
    public void ToDto_AssociatedProducts_ContainsDistinctPrefixes()
    {
        // Arrange
        var entry = new JournalEntry
        {
            Id = 1,
            Content = "c",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "u"
        };
        entry.ProductAssociations.Add(new JournalEntryProduct { ProductCodePrefix = "TON001" });
        entry.ProductAssociations.Add(new JournalEntryProduct { ProductCodePrefix = "TON001" }); // duplicate
        entry.ProductAssociations.Add(new JournalEntryProduct { ProductCodePrefix = "AKL002" });

        // Act
        var dto = JournalEntryMapper.ToDto(entry);

        // Assert
        dto.AssociatedProducts.Should().HaveCount(2);
        dto.AssociatedProducts.Should().Contain("TON001");
        dto.AssociatedProducts.Should().Contain("AKL002");
    }

    [Fact]
    public void ToDto_AssociatedProducts_IsEmptyList_WhenNoProducts()
    {
        // Arrange
        var entry = new JournalEntry
        {
            Id = 1,
            Content = "c",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "u"
        };

        // Act
        var dto = JournalEntryMapper.ToDto(entry);

        // Assert
        dto.AssociatedProducts.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ToDto_Tags_MapsIdNameColor()
    {
        // Arrange
        var tag = new JournalEntryTag { Id = 7, Name = "Urgent", Color = "#FFA500" };
        var entry = new JournalEntry
        {
            Id = 1,
            Content = "c",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "u"
        };
        entry.TagAssignments.Add(new JournalEntryTagAssignment { TagId = tag.Id, Tag = tag });

        // Act
        var dto = JournalEntryMapper.ToDto(entry);

        // Assert
        dto.Tags.Should().HaveCount(1);
        dto.Tags[0].Id.Should().Be(7);
        dto.Tags[0].Name.Should().Be("Urgent");
        dto.Tags[0].Color.Should().Be("#FFA500");
    }

    [Fact]
    public void ToDto_Tags_SkipsAssignmentsWithNullTag_AndDoesNotThrow()
    {
        // Arrange
        var goodTag = new JournalEntryTag { Id = 5, Name = "Valid", Color = "#123456" };
        var entry = new JournalEntry
        {
            Id = 1,
            Content = "c",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "u"
        };
        entry.TagAssignments.Add(new JournalEntryTagAssignment { TagId = 99, Tag = null! }); // orphan
        entry.TagAssignments.Add(new JournalEntryTagAssignment { TagId = 5, Tag = goodTag });

        // Act
        var act = () => JournalEntryMapper.ToDto(entry);

        // Assert
        act.Should().NotThrow();
        var dto = act();
        dto.Tags.Should().HaveCount(1);
        dto.Tags[0].Id.Should().Be(5);
    }

    [Fact]
    public void ToDto_Tags_IsEmptyList_WhenNoTagAssignments()
    {
        // Arrange
        var entry = new JournalEntry
        {
            Id = 1,
            Content = "c",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "u"
        };

        // Act
        var dto = JournalEntryMapper.ToDto(entry);

        // Assert
        dto.Tags.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ToSearchDto_MapsAllScalarFields_IncludingContent()
    {
        // Arrange
        var entry = BuildFullEntry();

        // Act
        var dto = JournalEntryMapper.ToSearchDto(entry);

        // Assert
        dto.Id.Should().Be(42);
        dto.Title.Should().Be("Test Entry");
        dto.Content.Should().Be("Test content body");
        dto.EntryDate.Should().Be(new DateTime(2025, 1, 15));
        dto.CreatedAt.Should().Be(new DateTime(2025, 1, 15, 10, 0, 0));
        dto.ModifiedAt.Should().Be(new DateTime(2025, 1, 15, 11, 0, 0));
        dto.CreatedByUserId.Should().Be("user-001");
        dto.CreatedByUsername.Should().Be("alice");
        dto.ModifiedByUserId.Should().Be("user-002");
        dto.ModifiedByUsername.Should().Be("bob");
    }

    [Fact]
    public void ToSearchDto_AssociatedProductsAndTags_AreMappedSameAsToDto()
    {
        // Arrange
        var entry = BuildFullEntry();

        // Act
        var dto = JournalEntryMapper.ToSearchDto(entry);

        // Assert
        dto.AssociatedProducts.Should().BeEquivalentTo(new[] { "TON001", "AKL002" });
        dto.Tags.Should().HaveCount(2);
        dto.Tags.Select(t => t.Id).Should().BeEquivalentTo(new[] { 10, 20 });
    }

    [Fact]
    public void ToSearchDto_PopulatesContentFromEntry()
    {
        // Arrange
        var entry = BuildFullEntry();

        // Act
        var dto = JournalEntryMapper.ToSearchDto(entry);

        // Assert
        dto.Content.Should().Be("Test content body");
    }
}
