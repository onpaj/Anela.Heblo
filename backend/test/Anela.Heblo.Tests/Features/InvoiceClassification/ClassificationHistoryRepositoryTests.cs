using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.InvoiceClassification;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification;

public class ClassificationHistoryRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ClassificationHistoryRepository _repository;

    public ClassificationHistoryRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"ClassificationHistoryTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new ClassificationHistoryRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task GetPagedHistoryAsync_WithTodayAsToDate_IncludesRecordsWithLaterTimestampOnSameDay()
    {
        // Reproduces the bug described in issue #513:
        // toDate is sent as midnight (00:00:00), so records from the same day with later
        // timestamps were incorrectly excluded. The fix extends toDate to start of next day.

        var referenceDate = new DateTime(2025, 12, 1); // midnight

        // Record from Dec 1 at 10:30 — should be included when toDate = Dec 1
        var history = new ClassificationHistory(
            abraInvoiceId: "PF250051",
            invoiceNumber: "PF250051",
            invoiceDate: referenceDate,
            companyName: "Pajgrt Ondrej",
            description: "Test invoice",
            result: ClassificationResult.Success,
            processedBy: "system");

        // Manually set Timestamp to 10:30 on Dec 1 (bypassing the private setter via reflection)
        var timestampProperty = typeof(ClassificationHistory).GetProperty("Timestamp")!;
        timestampProperty.SetValue(history, new DateTime(2025, 12, 1, 10, 30, 0));

        _context.ClassificationHistory.Add(history);
        await _context.SaveChangesAsync();

        // Act: filter with toDate = midnight of Dec 1 (as the frontend sends it)
        var (items, totalCount) = await _repository.GetPagedHistoryAsync(
            page: 1,
            pageSize: 20,
            toDate: referenceDate); // 2025-12-01 00:00:00

        // Assert: the record must be found even though its timestamp is 10:30 on the same day
        Assert.Equal(1, totalCount);
        Assert.Single(items);
        Assert.Equal("PF250051", items[0].AbraInvoiceId);
    }

    [Fact]
    public async Task GetPagedHistoryAsync_WithToDateExcludingNextDay_DoesNotIncludeRecordsFromFollowingDay()
    {
        var referenceDate = new DateTime(2025, 12, 1); // midnight

        // Record from Dec 2 — should NOT be included when toDate = Dec 1
        var history = new ClassificationHistory(
            abraInvoiceId: "PF250052",
            invoiceNumber: "PF250052",
            invoiceDate: referenceDate.AddDays(1),
            companyName: "Test Company",
            description: "Test invoice",
            result: ClassificationResult.Success,
            processedBy: "system");

        var timestampProperty = typeof(ClassificationHistory).GetProperty("Timestamp")!;
        timestampProperty.SetValue(history, new DateTime(2025, 12, 2, 8, 0, 0));

        _context.ClassificationHistory.Add(history);
        await _context.SaveChangesAsync();

        // Act
        var (items, totalCount) = await _repository.GetPagedHistoryAsync(
            page: 1,
            pageSize: 20,
            toDate: referenceDate); // toDate = Dec 1 midnight → endOfDay = Dec 2 midnight

        // Assert: Dec 2 record is excluded
        Assert.Equal(0, totalCount);
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetPagedHistoryAsync_WithAllFourFilters_ReturnsMatchingRecords()
    {
        // Simulate the combined-filter scenario from the E2E test
        var invoiceDate = new DateTime(2025, 12, 1);

        var matchingRecord = new ClassificationHistory(
            abraInvoiceId: "PF250051",
            invoiceNumber: "PF250051",
            invoiceDate: invoiceDate,
            companyName: "Pajgrt Ondrej",
            description: "Test",
            result: ClassificationResult.Success,
            processedBy: "system");

        var timestampProperty = typeof(ClassificationHistory).GetProperty("Timestamp")!;
        timestampProperty.SetValue(matchingRecord, new DateTime(2025, 12, 1, 10, 30, 0));

        var nonMatchingRecord = new ClassificationHistory(
            abraInvoiceId: "PF250099",
            invoiceNumber: "PF250099",
            invoiceDate: invoiceDate,
            companyName: "Another Company",
            description: "Test",
            result: ClassificationResult.Success,
            processedBy: "system");

        timestampProperty.SetValue(nonMatchingRecord, new DateTime(2025, 12, 1, 11, 0, 0));

        _context.ClassificationHistory.AddRange(matchingRecord, nonMatchingRecord);
        await _context.SaveChangesAsync();

        // Act: apply all four filters like the E2E test does
        var (items, totalCount) = await _repository.GetPagedHistoryAsync(
            page: 1,
            pageSize: 20,
            fromDate: new DateTime(2025, 12, 1),   // start of month
            toDate: new DateTime(2025, 12, 1),      // same day as record (midnight)
            invoiceNumber: "PF250051",
            companyName: "Pajgrt");

        // Assert: exactly the matching record is returned
        Assert.Equal(1, totalCount);
        Assert.Single(items);
        Assert.Equal("PF250051", items[0].AbraInvoiceId);
    }
}
