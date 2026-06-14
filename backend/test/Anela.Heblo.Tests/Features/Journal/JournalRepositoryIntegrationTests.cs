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

    // ---------- Sort matrix tests (FR-1 / FR-4) ----------

    private async Task SeedSortFixtureAsync()
    {
        // Three entries with deliberately distinct Title, CreatedAt, and EntryDate
        // values so each (sortBy, sortDirection) combination produces a unique ordering.
        var alpha = new JournalEntry
        {
            Title = "Alpha",
            Content = "alpha content",
            EntryDate = new DateTime(2024, 1, 1),
            CreatedAt = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = "test-user"
        };
        var bravo = new JournalEntry
        {
            Title = "Bravo",
            Content = "bravo content",
            EntryDate = new DateTime(2024, 2, 1),
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = "test-user"
        };
        var charlie = new JournalEntry
        {
            Title = "Charlie",
            Content = "charlie content",
            EntryDate = new DateTime(2024, 3, 1),
            CreatedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = "test-user"
        };

        await _context.Set<JournalEntry>().AddRangeAsync(alpha, bravo, charlie);
        await _context.SaveChangesAsync();
    }

    public static IEnumerable<object[]> SortMatrix()
    {
        // (sortBy, sortDirection, expectedTitlesInOrder)
        // Mapping of sortBy values:
        //   "title"             -> sort by Title
        //   "createdbyusername" -> sort by CreatedByUsername + tiebreak EntryDate DESC
        //   anything else (including null and unknown) -> sort by EntryDate (default)
        // Mapping of sortDirection:
        //   "ASC" (case-insensitive) -> ascending; anything else -> descending
        yield return new object[] { "title", "ASC", new[] { "Alpha", "Bravo", "Charlie" } };
        yield return new object[] { "title", "DESC", new[] { "Charlie", "Bravo", "Alpha" } };
        yield return new object[] { "title", "weird", new[] { "Charlie", "Bravo", "Alpha" } };
        yield return new object[] { "TITLE", "ASC", new[] { "Alpha", "Bravo", "Charlie" } };
        yield return new object[] { "unknown", "ASC", new[] { "Alpha", "Bravo", "Charlie" } };
        yield return new object[] { "unknown", "DESC", new[] { "Charlie", "Bravo", "Alpha" } };
        yield return new object[] { null!, "ASC", new[] { "Alpha", "Bravo", "Charlie" } };
        yield return new object[] { null!, "DESC", new[] { "Charlie", "Bravo", "Alpha" } };
        yield return new object[] { null!, "weird", new[] { "Charlie", "Bravo", "Alpha" } };
    }

    [Theory]
    [MemberData(nameof(SortMatrix))]
    public async Task GetEntriesAsync_AppliesExpectedOrdering(
        string? sortBy, string sortDirection, string[] expectedTitlesInOrder)
    {
        // Arrange
        await SeedSortFixtureAsync();

        // Act
        var result = await _repository.GetEntriesAsync(
            pageNumber: 1,
            pageSize: 10,
            sortBy: sortBy!,
            sortDirection: sortDirection);

        // Assert
        result.Should().NotBeNull();
        result.Items.Select(x => x.Title).Should().Equal(expectedTitlesInOrder);
        result.TotalCount.Should().Be(expectedTitlesInOrder.Length);
    }

    [Theory]
    [MemberData(nameof(SortMatrix))]
    public async Task SearchEntriesAsync_AppliesExpectedOrdering(
        string? sortBy, string sortDirection, string[] expectedTitlesInOrder)
    {
        // Arrange
        await SeedSortFixtureAsync();

        // Act
        // No filters supplied -> all three seeded rows should come back, ordered by the sort args.
        var result = await _repository.SearchEntriesAsync(
            searchText: null,
            dateFrom: null,
            dateTo: null,
            productCodePrefix: null,
            tagIds: null,
            createdByUserId: null,
            pageNumber: 1,
            pageSize: 10,
            sortBy: sortBy!,
            sortDirection: sortDirection);

        // Assert
        result.Should().NotBeNull();
        result.Items.Select(x => x.Title).Should().Equal(expectedTitlesInOrder);
        result.TotalCount.Should().Be(expectedTitlesInOrder.Length);
    }

    [Fact]
    public async Task GetEntriesAsync_SortsByCreatedByUsername_Ascending()
    {
        // Arrange
        var alice = CreateEntryWithAuthor("alice", DateTime.Today.AddDays(-1), "Alice entry");
        var carol = CreateEntryWithAuthor("carol", DateTime.Today.AddDays(-2), "Carol entry");
        var bob = CreateEntryWithAuthor("bob", DateTime.Today.AddDays(-3), "Bob entry");

        await _context.Set<JournalEntry>().AddRangeAsync(alice, carol, bob);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetEntriesAsync(
            pageNumber: 1,
            pageSize: 10,
            sortBy: "createdByUsername",
            sortDirection: "ASC");

        // Assert
        result.Items.Select(x => x.CreatedByUsername)
            .Should()
            .ContainInOrder("alice", "bob", "carol");
    }

    [Fact]
    public async Task GetEntriesAsync_SortsByCreatedByUsername_Descending()
    {
        // Arrange
        var alice = CreateEntryWithAuthor("alice", DateTime.Today.AddDays(-1), "Alice entry");
        var carol = CreateEntryWithAuthor("carol", DateTime.Today.AddDays(-2), "Carol entry");
        var bob = CreateEntryWithAuthor("bob", DateTime.Today.AddDays(-3), "Bob entry");

        await _context.Set<JournalEntry>().AddRangeAsync(alice, carol, bob);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetEntriesAsync(
            pageNumber: 1,
            pageSize: 10,
            sortBy: "createdByUsername",
            sortDirection: "DESC");

        // Assert
        result.Items.Select(x => x.CreatedByUsername)
            .Should()
            .ContainInOrder("carol", "bob", "alice");
    }

    [Fact]
    public async Task GetEntriesAsync_SortsByCreatedByUsername_AcceptsAnyCasing()
    {
        // Arrange
        var alice = CreateEntryWithAuthor("alice", DateTime.Today.AddDays(-1), "Alice entry");
        var bob = CreateEntryWithAuthor("bob", DateTime.Today.AddDays(-2), "Bob entry");

        await _context.Set<JournalEntry>().AddRangeAsync(alice, bob);
        await _context.SaveChangesAsync();

        // Act
        var upper = await _repository.GetEntriesAsync(1, 10, "CREATEDBYUSERNAME", "ASC");
        var mixed = await _repository.GetEntriesAsync(1, 10, "CreatedByUsername", "ASC");
        var lower = await _repository.GetEntriesAsync(1, 10, "createdbyusername", "ASC");

        // Assert
        upper.Items.Select(x => x.CreatedByUsername).Should().ContainInOrder("alice", "bob");
        mixed.Items.Select(x => x.CreatedByUsername).Should().ContainInOrder("alice", "bob");
        lower.Items.Select(x => x.CreatedByUsername).Should().ContainInOrder("alice", "bob");
    }

    [Fact]
    public async Task GetEntriesAsync_SortByCreatedByUsername_TiebreaksByEntryDateDesc()
    {
        // Arrange
        var aliceOlder = CreateEntryWithAuthor("alice", DateTime.Today.AddDays(-5), "Alice older");
        var aliceNewer = CreateEntryWithAuthor("alice", DateTime.Today.AddDays(-1), "Alice newer");
        var bob = CreateEntryWithAuthor("bob", DateTime.Today.AddDays(-3), "Bob entry");

        await _context.Set<JournalEntry>().AddRangeAsync(aliceOlder, aliceNewer, bob);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetEntriesAsync(1, 10, "createdByUsername", "ASC");

        // Assert
        result.Items.Select(x => x.Title)
            .Should()
            .ContainInOrder("Alice newer", "Alice older", "Bob entry");
    }

    [Fact]
    public async Task GetEntriesAsync_UnknownSortBy_LogsWarningWithStructuredProperty()
    {
        // Arrange
        await _context.Set<JournalEntry>().AddAsync(
            CreateEntryWithAuthor("alice", DateTime.Today, "Any entry"));
        await _context.SaveChangesAsync();

        // Act — "tags" is not handled; should default-sort AND log a warning.
        var result = await _repository.GetEntriesAsync(1, 10, "tags", "ASC");

        // Assert — call succeeded with the default sort applied.
        result.Items.Should().HaveCount(1);

        // Assert — exactly one Warning was logged, message references the sortBy value.
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("tags")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetEntriesAsync_NullSortBy_DoesNotLogWarning()
    {
        // Arrange
        await _context.Set<JournalEntry>().AddAsync(
            CreateEntryWithAuthor("alice", DateTime.Today, "Any entry"));
        await _context.SaveChangesAsync();

        // Act
#pragma warning disable CS8625
        var result = await _repository.GetEntriesAsync(1, 10, sortBy: null, sortDirection: "ASC");
#pragma warning restore CS8625

        // Assert
        result.Items.Should().HaveCount(1);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task GetEntriesAsync_EmptySortBy_DoesNotLogWarning()
    {
        // Arrange
        await _context.Set<JournalEntry>().AddAsync(
            CreateEntryWithAuthor("alice", DateTime.Today, "Any entry"));
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetEntriesAsync(1, 10, sortBy: "", sortDirection: "ASC");

        // Assert
        result.Items.Should().HaveCount(1);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task GetEntriesAsync_WhitespaceSortBy_DoesNotLogWarning()
    {
        // Arrange
        await _context.Set<JournalEntry>().AddAsync(
            CreateEntryWithAuthor("alice", DateTime.Today, "Any entry"));
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetEntriesAsync(1, 10, sortBy: "   ", sortDirection: "ASC");

        // Assert
        result.Items.Should().HaveCount(1);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchEntriesAsync_SortsByCreatedByUsername_Ascending()
    {
        // Arrange — same setup as GetEntriesAsync_SortsByCreatedByUsername_Ascending.
        var alice = CreateEntryWithAuthor("alice", DateTime.Today.AddDays(-1), "Alice entry");
        var carol = CreateEntryWithAuthor("carol", DateTime.Today.AddDays(-2), "Carol entry");
        var bob = CreateEntryWithAuthor("bob", DateTime.Today.AddDays(-3), "Bob entry");

        await _context.Set<JournalEntry>().AddRangeAsync(alice, carol, bob);
        await _context.SaveChangesAsync();

        // Act — search path with no filters; sort by author ascending.
        var result = await _repository.SearchEntriesAsync(
            searchText: null,
            dateFrom: null,
            dateTo: null,
            productCodePrefix: null,
            tagIds: null,
            createdByUserId: null,
            pageNumber: 1,
            pageSize: 10,
            sortBy: "createdByUsername",
            sortDirection: "ASC");

        // Assert
        result.Items.Select(x => x.CreatedByUsername)
            .Should()
            .ContainInOrder("alice", "bob", "carol");
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

    private static JournalEntry CreateEntryWithAuthor(
        string author,
        DateTime entryDate,
        string title)
    {
        return new JournalEntry
        {
            Title = title,
            Content = $"Content authored by {author}",
            EntryDate = entryDate,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user-id",
            CreatedByUsername = author
        };
    }

    public void Dispose()
    {
        _context?.Database?.EnsureDeleted();
        _context?.Dispose();
    }
}
