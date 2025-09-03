using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Repositories;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Repositories;

public class TransportBoxRepositoryCaseHandlingTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly TransportBoxRepository _repository;
    private readonly Mock<ILogger<TransportBoxRepository>> _loggerMock;
    private const string TestUser = "TestUser";
    private readonly DateTime _testDate = DateTime.UtcNow;

    public TransportBoxRepositoryCaseHandlingTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _loggerMock = new Mock<ILogger<TransportBoxRepository>>();
        _repository = new TransportBoxRepository(_context, _loggerMock.Object);

        // Seed test data
        SeedTestData();
    }

    private void SeedTestData()
    {
        var boxes = new List<TransportBox>();

        // Create boxes with uppercase codes
        var box1 = new TransportBox();
        box1.Open("B001", _testDate, TestUser);
        boxes.Add(box1);

        var box2 = new TransportBox();
        box2.Open("B123", _testDate, TestUser);
        boxes.Add(box2);

        var box3 = new TransportBox();
        box3.Open("B999", _testDate, TestUser);
        boxes.Add(box3);

        _context.TransportBoxes.AddRange(boxes);
        _context.SaveChanges();
    }

    [Theory]
    [InlineData("B001", 1)]
    [InlineData("b001", 1)]
    [InlineData("B123", 1)]
    [InlineData("b123", 1)]
    [InlineData("B999", 1)]
    [InlineData("b999", 1)]
    [InlineData("B", 3)] // Should find all boxes starting with B
    [InlineData("b", 3)] // Should find all boxes starting with B (case insensitive)
    [InlineData("001", 1)] // Should find B001
    [InlineData("123", 1)] // Should find B123
    [InlineData("999", 1)] // Should find B999
    [InlineData("X999", 0)] // Should find nothing
    public async Task GetPagedListAsync_WithCodeFilter_ShouldBeCaseInsensitive(string codeFilter, int expectedCount)
    {
        // Act
        var (items, totalCount) = await _repository.GetPagedListAsync(
            skip: 0,
            take: 10,
            code: codeFilter);

        // Assert
        items.Should().HaveCount(expectedCount);
        totalCount.Should().Be(expectedCount);

        if (expectedCount > 0)
        {
            items.Should().AllSatisfy(box => 
                box.Code!.ToUpper().Should().Contain(codeFilter.ToUpper()));
        }
    }

    [Theory]
    [InlineData("B001")]
    [InlineData("b001")]
    [InlineData("B123")]
    [InlineData("b123")]
    [InlineData("B999")]
    [InlineData("b999")]
    public async Task IsBoxCodeActiveAsync_WithMixedCase_ShouldFindMatch(string searchCode)
    {
        // Act
        var isActive = await _repository.IsBoxCodeActiveAsync(searchCode);

        // Assert
        // After fix: both uppercase and lowercase should find matches
        isActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("B001")]
    [InlineData("b001")]
    [InlineData("B123")]
    [InlineData("b123")]
    [InlineData("B999")]
    [InlineData("b999")]
    public async Task GetByCodeAsync_WithMixedCase_ShouldFindMatch(string searchCode)
    {
        // Act
        var box = await _repository.GetByCodeAsync(searchCode);

        // Assert
        // After fix: both uppercase and lowercase should find matches
        box.Should().NotBeNull();
        box!.Code.Should().Be(searchCode.ToUpper());
    }

    [Fact]
    public async Task GetPagedListAsync_WithEmptyCodeFilter_ShouldReturnAllBoxes()
    {
        // Act
        var (items, totalCount) = await _repository.GetPagedListAsync(
            skip: 0,
            take: 10,
            code: "");

        // Assert
        items.Should().HaveCount(3);
        totalCount.Should().Be(3);
    }

    [Fact]
    public async Task GetPagedListAsync_WithNullCodeFilter_ShouldReturnAllBoxes()
    {
        // Act
        var (items, totalCount) = await _repository.GetPagedListAsync(
            skip: 0,
            take: 10,
            code: null);

        // Assert
        items.Should().HaveCount(3);
        totalCount.Should().Be(3);
    }

    [Theory]
    [InlineData("b00", 1)] // Should find B001
    [InlineData("B00", 1)] // Should find B001
    [InlineData("12", 1)]  // Should find B123
    [InlineData("99", 1)]  // Should find B999
    public async Task GetPagedListAsync_WithPartialCodeFilter_ShouldBeCaseInsensitive(string partialCode, int expectedCount)
    {
        // Act
        var (items, totalCount) = await _repository.GetPagedListAsync(
            skip: 0,
            take: 10,
            code: partialCode);

        // Assert
        items.Should().HaveCount(expectedCount);
        totalCount.Should().Be(expectedCount);
        
        items.Should().AllSatisfy(box => 
            box.Code!.ToUpper().Should().Contain(partialCode.ToUpper()));
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}