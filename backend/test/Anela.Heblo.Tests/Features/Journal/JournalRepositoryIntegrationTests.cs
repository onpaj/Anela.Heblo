using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.Infrastructure;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class JournalRepositoryIntegrationTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly JournalRepository _repository;
    private readonly Mock<ILogger<JournalRepository>> _loggerMock;

    public JournalRepositoryIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"JournalTestDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _loggerMock = new Mock<ILogger<JournalRepository>>();
        _repository = new JournalRepository(_context, _loggerMock.Object);
    }

    [Fact]
    public async Task GetEntriesByProductAsync_WithProductCodePrefix_ShouldFindMatchingEntries()
    {
        // Arrange
        // Create journal entry associated with product family "TON002"
        var entry = new JournalEntry
        {
            Title = "Note about TON002 product family",
            Content = "This applies to all TON002 products including TON002030",
            EntryDate = DateTime.Now,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user"
        };
        entry.AssociateWithProduct("TON002");

        await _context.Set<JournalEntry>().AddAsync(entry);
        await _context.SaveChangesAsync();

        // Act - Test using GetEntriesByProductAsync which should find family entries
        var result = await _repository.GetEntriesByProductAsync("TON002030");

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1, "TON002030 starts with TON002, so should find the family entry");
        result.First().Title.Should().Be("Note about TON002 product family");
        result.First().ProductAssociations.Should().HaveCount(1);
        result.First().ProductAssociations.First().ProductCodePrefix.Should().Be("TON002");
    }

    [Fact]
    public async Task GetEntriesByProductAsync_WithProductCode_ShouldFindFamilyEntries()
    {
        // Arrange
        // Create journal entry associated with product family "TON002"
        var familyEntry = new JournalEntry
        {
            Title = "Family note for TON002",
            Content = "Applies to all TON002 products",
            EntryDate = DateTime.Now,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user"
        };
        familyEntry.AssociateWithProduct("TON002");

        // Create journal entry for specific product
        var specificEntry = new JournalEntry
        {
            Title = "Specific note for TON002030",
            Content = "Only for TON002030",
            EntryDate = DateTime.Now.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ModifiedAt = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = "test-user"
        };
        specificEntry.AssociateWithProduct("TON002030");

        // Create unrelated entry
        var unrelatedEntry = new JournalEntry
        {
            Title = "Unrelated note",
            Content = "For different product",
            EntryDate = DateTime.Now.AddDays(-2),
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            ModifiedAt = DateTime.UtcNow.AddDays(-2),
            CreatedByUserId = "test-user"
        };
        unrelatedEntry.AssociateWithProduct("CREAM001");

        await _context.Set<JournalEntry>().AddRangeAsync(familyEntry, specificEntry, unrelatedEntry);
        await _context.SaveChangesAsync();

        // Act - search for product "TON002030"
        var result = await _repository.GetEntriesByProductAsync("TON002030");

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2); // Should find both specific and family entries
        result.Should().Contain(e => e.Title == "Specific note for TON002030");
        result.Should().Contain(e => e.Title == "Family note for TON002");
        result.Should().NotContain(e => e.Title == "Unrelated note");
    }

    [Fact]
    public async Task GetEntriesByProductAsync_ProductStartsWithPrefix_ShouldMatchFamilyEntry()
    {
        // This is the critical test for the issue:
        // Product "TON002030" should find entries with prefix "TON002"

        // Arrange
        var entry = new JournalEntry
        {
            Title = "TON002 family documentation",
            Content = "Documentation for all TON002 products",
            EntryDate = DateTime.Now,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user"
        };
        entry.AssociateWithProduct("TON002");

        await _context.Set<JournalEntry>().AddAsync(entry);
        await _context.SaveChangesAsync();

        // Act - search for specific product that starts with the family prefix
        var result = await _repository.GetEntriesByProductAsync("TON002030");

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1, "TON002030 starts with TON002, so it should match the family entry");
        result.First().Title.Should().Be("TON002 family documentation");
    }

    [Fact]
    public async Task GetEntriesByProductAsync_DifferentPrefix_ShouldNotMatch()
    {
        // Arrange
        var entry = new JournalEntry
        {
            Title = "CREAM family documentation",
            Content = "Documentation for CREAM products",
            EntryDate = DateTime.Now,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user"
        };
        entry.AssociateWithProduct("CREAM");

        await _context.Set<JournalEntry>().AddAsync(entry);
        await _context.SaveChangesAsync();

        // Act - search for product that doesn't start with CREAM
        var result = await _repository.GetEntriesByProductAsync("TON002030");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty("TON002030 doesn't start with CREAM");
    }

    [Fact]
    public async Task GetEntriesByProductAsync_MultipleProducts_ShouldFindCorrectFamilyEntries()
    {
        // Arrange
        var entry1 = CreateEntryWithFamily("TON001", "TON001 family note");
        var entry2 = CreateEntryWithFamily("TON002", "TON002 family note");
        var entry3 = CreateEntryWithFamily("CREAM", "CREAM family note");

        await _context.Set<JournalEntry>().AddRangeAsync(entry1, entry2, entry3);
        await _context.SaveChangesAsync();

        // Act - Test both products should find their respective family entries
        var result1 = await _repository.GetEntriesByProductAsync("TON001030");
        var result2 = await _repository.GetEntriesByProductAsync("TON002030");
        var result3 = await _repository.GetEntriesByProductAsync("CREAM001");

        // Assert
        result1.Should().HaveCount(1);
        result1.First().Title.Should().Be("TON001 family note");

        result2.Should().HaveCount(1);
        result2.First().Title.Should().Be("TON002 family note");

        result3.Should().HaveCount(1);
        result3.First().Title.Should().Be("CREAM family note");
    }

    private JournalEntry CreateEntryWithFamily(string prefix, string title)
    {
        var entry = new JournalEntry
        {
            Title = title,
            Content = $"Content for {prefix} family",
            EntryDate = DateTime.Now,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user"
        };
        entry.AssociateWithProduct(prefix);
        return entry;
    }

    public void Dispose()
    {
        _context?.Database?.EnsureDeleted();
        _context?.Dispose();
    }
}