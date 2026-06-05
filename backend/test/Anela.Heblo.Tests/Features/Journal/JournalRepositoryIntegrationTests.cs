using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Journal;
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

    [Fact]
    public async Task GetJournalIndicatorsAsync_WithMultipleDirectEntries_ReturnsCorrectCount()
    {
        // Arrange
        var latest = DateTime.Today;
        var middle = DateTime.Today.AddDays(-1);
        var earliest = DateTime.Today.AddDays(-2);

        var e1 = new JournalEntry
        {
            Title = "TON002 entry 1",
            Content = "Content",
            EntryDate = latest,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user"
        };
        e1.AssociateWithProduct("TON002");

        var e2 = new JournalEntry
        {
            Title = "TON002 entry 2",
            Content = "Content",
            EntryDate = middle,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user"
        };
        e2.AssociateWithProduct("TON002");

        var e3 = new JournalEntry
        {
            Title = "TON002 entry 3",
            Content = "Content",
            EntryDate = earliest,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user"
        };
        e3.AssociateWithProduct("TON002");

        await _context.Set<JournalEntry>().AddRangeAsync(e1, e2, e3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetJournalIndicatorsAsync(new[] { "TON002" });

        // Assert
        result.Should().ContainKey("TON002");
        var indicator = result["TON002"];
        indicator.DirectEntries.Should().Be(3);
        indicator.LastEntryDate.Should().Be(latest);
        indicator.HasRecentEntries.Should().BeTrue();
    }

    [Fact]
    public async Task GetJournalIndicatorsAsync_WithNoEntries_ReturnsZeroIndicator()
    {
        // Arrange — intentionally no entries inserted

        // Act
        var result = await _repository.GetJournalIndicatorsAsync(new[] { "UNUSED999" });

        // Assert
        result.Should().ContainKey("UNUSED999");
        var indicator = result["UNUSED999"];
        indicator.DirectEntries.Should().Be(0);
        indicator.LastEntryDate.Should().BeNull();
        indicator.HasRecentEntries.Should().BeFalse();
    }

    [Fact]
    public async Task GetJournalIndicatorsAsync_WithRecentEntry_FlagsHasRecentEntries()
    {
        // Arrange
        var recent = DateTime.Today.AddDays(-5);
        var entry = new JournalEntry
        {
            Title = "Recent CREAM001 entry",
            Content = "Content",
            EntryDate = recent,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user"
        };
        entry.AssociateWithProduct("CREAM001");
        await _context.Set<JournalEntry>().AddAsync(entry);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetJournalIndicatorsAsync(new[] { "CREAM001" });

        // Assert
        result.Should().ContainKey("CREAM001");
        var indicator = result["CREAM001"];
        indicator.DirectEntries.Should().Be(1);
        indicator.HasRecentEntries.Should().BeTrue();
        indicator.LastEntryDate.Should().Be(recent);
    }

    [Fact]
    public async Task GetByIdAsync_WhenEntryIsSoftDeleted_ReturnsNull()
    {
        // Arrange
        var entry = new JournalEntry
        {
            Title = "Soft-deleted entry",
            Content = "Content",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
            DeletedByUserId = "test-user"
        };
        await _context.Set<JournalEntry>().AddAsync(entry);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdAsync(entry.Id);

        // Assert
        result.Should().BeNull("soft-deleted entries must be excluded by the global query filter");
    }

    [Fact]
    public async Task GetEntriesAsync_WhenEntryIsSoftDeleted_ExcludesFromResults()
    {
        // Arrange
        var live = new JournalEntry
        {
            Title = "Live entry",
            Content = "Content",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user"
        };
        var deleted = new JournalEntry
        {
            Title = "Deleted entry",
            Content = "Content",
            EntryDate = DateTime.Today.AddDays(-1),
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
            DeletedByUserId = "test-user"
        };
        await _context.Set<JournalEntry>().AddRangeAsync(live, deleted);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetEntriesAsync(pageNumber: 1, pageSize: 50, sortBy: "entrydate", sortDirection: "DESC");

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(e => e.Title == "Live entry");
        result.Items.Should().NotContain(e => e.Title == "Deleted entry");
    }

    [Fact]
    public async Task SearchEntriesAsync_WhenEntryIsSoftDeleted_ExcludesFromResults()
    {
        // Arrange
        var live = new JournalEntry
        {
            Title = "Searchable live",
            Content = "matching term",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user"
        };
        var deleted = new JournalEntry
        {
            Title = "Searchable deleted",
            Content = "matching term",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
            DeletedByUserId = "test-user"
        };
        await _context.Set<JournalEntry>().AddRangeAsync(live, deleted);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.SearchEntriesAsync(
            searchText: "matching",
            dateFrom: null,
            dateTo: null,
            productCodePrefix: null,
            tagIds: null,
            createdByUserId: null,
            pageNumber: 1,
            pageSize: 50,
            sortBy: "entrydate",
            sortDirection: "DESC");

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(e => e.Title == "Searchable live");
        result.Items.Should().NotContain(e => e.Title == "Searchable deleted");
    }

    [Fact]
    public async Task GetEntriesByProductAsync_WhenEntryIsSoftDeleted_ExcludesFromResults()
    {
        // Arrange
        var live = new JournalEntry
        {
            Title = "Live TON002 entry",
            Content = "Content",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user"
        };
        live.AssociateWithProduct("TON002");

        var deleted = new JournalEntry
        {
            Title = "Deleted TON002 entry",
            Content = "Content",
            EntryDate = DateTime.Today.AddDays(-1),
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
            DeletedByUserId = "test-user"
        };
        deleted.AssociateWithProduct("TON002");

        await _context.Set<JournalEntry>().AddRangeAsync(live, deleted);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetEntriesByProductAsync("TON002030");

        // Assert
        result.Should().ContainSingle();
        result.Single().Title.Should().Be("Live TON002 entry");
    }

    [Fact]
    public async Task GetJournalIndicatorsAsync_WhenEntryIsSoftDeleted_ExcludesFromCount()
    {
        // Arrange — verifies the join source honors the global query filter
        var deleted = new JournalEntry
        {
            Title = "Deleted TON002 entry",
            Content = "Content",
            EntryDate = DateTime.Today,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
            DeletedByUserId = "test-user"
        };
        deleted.AssociateWithProduct("TON002");

        await _context.Set<JournalEntry>().AddAsync(deleted);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetJournalIndicatorsAsync(new[] { "TON002" });

        // Assert
        result.Should().ContainKey("TON002");
        var indicator = result["TON002"];
        indicator.DirectEntries.Should().Be(0, "soft-deleted entries must not count toward indicators");
        indicator.LastEntryDate.Should().BeNull();
        indicator.HasRecentEntries.Should().BeFalse();
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