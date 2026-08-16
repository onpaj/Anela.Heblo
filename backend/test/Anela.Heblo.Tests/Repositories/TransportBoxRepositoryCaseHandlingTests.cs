using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Logistics.TransportBoxes;
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

    private TransportBox NewBoxWithCode(string code)
    {
        var box = new TransportBox();
        box.AssignBoxCodeIfAny(code);
        return box;
    }

    private TransportBox OpenedBox(string code)
    {
        var box = new TransportBox();
        box.Open(code, _testDate, TestUser);
        return box;
    }

    private TransportBox InTransitBox(string code)
    {
        var box = OpenedBox(code);
        box.AddItem("P-1", "P", 1, _testDate, TestUser);
        box.ToTransit(_testDate, TestUser);
        return box;
    }

    private TransportBox ReceivedBox(string code)
    {
        var box = InTransitBox(code);
        box.Receive(_testDate, TestUser);
        return box;
    }

    private TransportBox StockedBox(string code)
    {
        var box = ReceivedBox(code);
        box.ToPick(_testDate, TestUser);
        return box;
    }

    private TransportBox ClosedBox(string code)
    {
        var box = StockedBox(code);
        box.Close(_testDate, TestUser);
        return box;
    }

    private TransportBox ReserveBox(string code)
    {
        var box = OpenedBox(code);
        box.ToReserve(_testDate, TestUser, "L1");
        return box;
    }

    private TransportBox QuarantineBox(string code)
    {
        var box = OpenedBox(code);
        box.ToQuarantine(_testDate, TestUser);
        return box;
    }

    private TransportBox ErrorBox(string code)
    {
        var box = OpenedBox(code);
        box.Error(_testDate, TestUser, "boom");
        return box;
    }

    [Fact]
    public async Task IsBoxCodeActiveAsync_QuarantineBox_ReturnsTrue()
    {
        _context.TransportBoxes.Add(QuarantineBox("B500"));
        await _context.SaveChangesAsync();

        (await _repository.IsBoxCodeActiveAsync("B500")).Should().BeTrue();
    }

    [Fact]
    public async Task IsBoxCodeActiveAsync_ErrorBox_ReturnsTrue()
    {
        _context.TransportBoxes.Add(ErrorBox("B501"));
        await _context.SaveChangesAsync();

        (await _repository.IsBoxCodeActiveAsync("B501")).Should().BeTrue();
    }

    [Fact]
    public async Task IsBoxCodeActiveAsync_NewBoxWithCode_ReturnsTrue()
    {
        _context.TransportBoxes.Add(NewBoxWithCode("B502"));
        await _context.SaveChangesAsync();

        (await _repository.IsBoxCodeActiveAsync("B502")).Should().BeTrue();
    }

    [Fact]
    public async Task IsBoxCodeActiveAsync_OpenedBox_ReturnsTrue()
    {
        _context.TransportBoxes.Add(OpenedBox("B503"));
        await _context.SaveChangesAsync();

        (await _repository.IsBoxCodeActiveAsync("B503")).Should().BeTrue();
    }

    [Fact]
    public async Task IsBoxCodeActiveAsync_InTransitBox_ReturnsTrue()
    {
        _context.TransportBoxes.Add(InTransitBox("B504"));
        await _context.SaveChangesAsync();

        (await _repository.IsBoxCodeActiveAsync("B504")).Should().BeTrue();
    }

    [Fact]
    public async Task IsBoxCodeActiveAsync_ReceivedBox_ReturnsTrue()
    {
        _context.TransportBoxes.Add(ReceivedBox("B505"));
        await _context.SaveChangesAsync();

        (await _repository.IsBoxCodeActiveAsync("B505")).Should().BeTrue();
    }

    [Fact]
    public async Task IsBoxCodeActiveAsync_ReserveBox_ReturnsTrue()
    {
        _context.TransportBoxes.Add(ReserveBox("B506"));
        await _context.SaveChangesAsync();

        (await _repository.IsBoxCodeActiveAsync("B506")).Should().BeTrue();
    }

    [Fact]
    public async Task IsBoxCodeActiveAsync_StockedBox_ReturnsFalse()
    {
        _context.TransportBoxes.Add(StockedBox("B507"));
        await _context.SaveChangesAsync();

        (await _repository.IsBoxCodeActiveAsync("B507")).Should().BeFalse();
    }

    [Fact]
    public async Task IsBoxCodeActiveAsync_ClosedBox_ReturnsFalse()
    {
        _context.TransportBoxes.Add(ClosedBox("B508"));
        await _context.SaveChangesAsync();

        (await _repository.IsBoxCodeActiveAsync("B508")).Should().BeFalse();
    }

    [Fact]
    public async Task IsBoxCodeActiveAsync_CodeHeldByNobody_ReturnsFalse()
    {
        (await _repository.IsBoxCodeActiveAsync("B777")).Should().BeFalse();
    }

    [Fact]
    public async Task GetByCodeAsync_StockedBoxWithHigherId_ReturnsOccupyingOpenedBox()
    {
        // Opened box saved FIRST so it gets the LOWER Id; Stocked box saved second (higher Id).
        var opened = OpenedBox("B510");
        _context.TransportBoxes.Add(opened);
        await _context.SaveChangesAsync();

        var stocked = StockedBox("B510");
        _context.TransportBoxes.Add(stocked);
        await _context.SaveChangesAsync();

        stocked.Id.Should().BeGreaterThan(opened.Id, "the test's premise is that the released box is newer");

        var found = await _repository.GetByCodeAsync("B510");

        found.Should().NotBeNull();
        found!.Id.Should().Be(opened.Id);
        found.State.Should().Be(TransportBoxState.Opened);
    }

    [Fact]
    public async Task GetByCodeAsync_OnlyReleasedBoxes_ReturnsNewest()
    {
        // No occupying box: Id-descending still wins, i.e. no behaviour change for released-only data.
        var closed = ClosedBox("B511");
        _context.TransportBoxes.Add(closed);
        await _context.SaveChangesAsync();

        var stocked = StockedBox("B511");
        _context.TransportBoxes.Add(stocked);
        await _context.SaveChangesAsync();

        stocked.Id.Should().BeGreaterThan(closed.Id);

        var found = await _repository.GetByCodeAsync("B511");

        found.Should().NotBeNull();
        found!.Id.Should().Be(stocked.Id);
        found.State.Should().Be(TransportBoxState.Stocked);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}