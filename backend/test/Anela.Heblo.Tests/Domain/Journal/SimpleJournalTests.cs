using Anela.Heblo.Domain.Features.Journal;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Journal;

public class SimpleJournalTests
{
    [Fact]
    public void JournalEntry_CanBeCreated()
    {
        // Arrange & Act
        var entry = new JournalEntry
        {
            Title = "Test Entry",
            Content = "Test content",
            EntryDate = DateTime.Now,
            CreatedByUserId = "user123",
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        // Assert
        entry.Should().NotBeNull();
        entry.Title.Should().Be("Test Entry");
        entry.Content.Should().Be("Test content");
        entry.CreatedByUserId.Should().Be("user123");
    }

    [Fact]
    public void AssociateWithProduct_ShouldAddProductAssociation()
    {
        // Arrange
        var entry = new JournalEntry
        {
            Content = "Test content",
            EntryDate = DateTime.Now,
            CreatedByUserId = "user123"
        };

        // Act
        entry.AssociateWithProduct("PROD123");

        // Assert
        entry.ProductAssociations.Should().HaveCount(1);
        entry.ProductAssociations.First().ProductCodePrefix.Should().Be("PROD123");
    }

    [Fact]
    public void AssociateWithProductFamily_ShouldAddProductFamilyAssociation()
    {
        // Arrange
        var entry = new JournalEntry
        {
            Content = "Test content",
            EntryDate = DateTime.Now,
            CreatedByUserId = "user123"
        };

        // Act
        entry.AssociateWithProduct("CREAM");

        // Assert
        entry.ProductAssociations.Should().HaveCount(1);
        entry.ProductAssociations.First().ProductCodePrefix.Should().Be("CREAM");
    }

    [Fact]
    public void SoftDelete_ShouldMarkEntryAsDeleted()
    {
        // Arrange
        var entry = new JournalEntry
        {
            Content = "Test content",
            EntryDate = DateTime.Now,
            CreatedByUserId = "user123"
        };

        // Act
        entry.SoftDelete("admin", "Admin User");

        // Assert
        entry.IsDeleted.Should().BeTrue();
        entry.DeletedByUserId.Should().Be("admin");
        entry.DeletedAt.Should().NotBeNull();
        entry.ModifiedByUserId.Should().Be("admin");
    }
}