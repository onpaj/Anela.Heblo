using Anela.Heblo.Adapters.Flexi.Purchase;
using Anela.Heblo.Domain.Entities;
using Anela.Heblo.Xcc.Audit;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Contacts;
using Rem.FlexiBeeSDK.Model.Contacts;

namespace Anela.Heblo.Adapters.Flexi.Tests.Purchase;

public class FlexiSupplierRepositoryTests
{
    private readonly Mock<IContactListClient> _mockContactListClient;
    private readonly Mock<IDataLoadAuditService> _mockAuditService;
    private readonly Mock<ILogger<FlexiSupplierRepository>> _mockLogger;
    private readonly IMemoryCache _memoryCache;
    private readonly FlexiSupplierRepository _repository;

    private readonly List<ContactFlexiDto> _testContacts = new()
    {
        new ContactFlexiDto
        {
            Id = 1,
            Name = "ABC Company Ltd",
            Code = "ABC001",
            Note = "Test supplier 1",
            Email = "abc@example.com",
            Phone = "+420123456789",
            Website = "https://abc.com"
        },
        new ContactFlexiDto
        {
            Id = 2,
            Name = "XYZ Corporation",
            Code = "XYZ002",
            Note = "Test supplier 2",
            Email = "xyz@example.com",
            Phone = "+420987654321",
            Website = "https://xyz.com"
        },
        new ContactFlexiDto
        {
            Id = 3,
            Name = "DEF Industries",
            Code = "DEF003",
            Note = "Test supplier 3",
            Email = "def@example.com",
            Phone = "+420555666777",
            Website = "https://def.com"
        }
    };

    public FlexiSupplierRepositoryTests()
    {
        _mockContactListClient = new Mock<IContactListClient>();
        _mockAuditService = new Mock<IDataLoadAuditService>();
        _mockLogger = new Mock<ILogger<FlexiSupplierRepository>>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());

        _repository = new FlexiSupplierRepository(
            _mockContactListClient.Object,
            _mockAuditService.Object,
            _mockLogger.Object,
            _memoryCache);
    }

    [Fact]
    public async Task SearchSuppliersAsync_CacheMiss_LoadsFromContactListClient()
    {
        // Arrange
        _mockContactListClient
            .Setup(x => x.GetAsync(It.Is<ContactType>(ct => ct == ContactType.Supplier), It.Is<int>(i => i == 0), It.Is<int>(i => i == 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testContacts);

        // Act
        var result = await _repository.SearchSuppliersAsync("ABC", 10, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("ABC Company Ltd");
        result[0].Code.Should().Be("ABC001");

        _mockContactListClient.Verify(
            x => x.GetAsync(It.Is<ContactType>(ct => ct == ContactType.Supplier), It.Is<int>(i => i == 0), It.Is<int>(i => i == 0), It.IsAny<CancellationToken>()),
            Times.Once);

        _mockAuditService.Verify(
            x => x.LogDataLoadAsync(
                "All Suppliers Load",
                "Flexi ERP",
                3,
                true,
                It.IsAny<Dictionary<string, object>>(),
                null,
                It.IsAny<TimeSpan>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchSuppliersAsync_CacheHit_DoesNotCallContactListClient()
    {
        // Arrange - First call to populate cache
        _mockContactListClient
            .Setup(x => x.GetAsync(It.Is<ContactType>(ct => ct == ContactType.Supplier), It.Is<int>(i => i == 0), It.Is<int>(i => i == 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testContacts);

        await _repository.SearchSuppliersAsync("ABC", 10, CancellationToken.None);

        // Act - Second call should use cache
        var result = await _repository.SearchSuppliersAsync("XYZ", 10, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("XYZ Corporation");

        _mockContactListClient.Verify(
            x => x.GetAsync(It.Is<ContactType>(ct => ct == ContactType.Supplier), It.Is<int>(i => i == 0), It.Is<int>(i => i == 0), It.IsAny<CancellationToken>()),
            Times.Once); // Only called once, not twice
    }

    [Fact]
    public async Task SearchSuppliersAsync_SearchByName_CaseInsensitive_ReturnsMatchingSuppliers()
    {
        // Arrange
        _mockContactListClient
            .Setup(x => x.GetAsync(It.Is<ContactType>(ct => ct == ContactType.Supplier), It.Is<int>(i => i == 0), It.Is<int>(i => i == 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testContacts);

        // Act - Search with lowercase term
        var result = await _repository.SearchSuppliersAsync("abc", 10, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("ABC Company Ltd");
    }

    [Fact]
    public async Task SearchSuppliersAsync_SearchByCode_CaseInsensitive_ReturnsMatchingSuppliers()
    {
        // Arrange
        _mockContactListClient
            .Setup(x => x.GetAsync(It.Is<ContactType>(ct => ct == ContactType.Supplier), It.Is<int>(i => i == 0), It.Is<int>(i => i == 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testContacts);

        // Act - Search with lowercase code
        var result = await _repository.SearchSuppliersAsync("xyz002", 10, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].Code.Should().Be("XYZ002");
        result[0].Name.Should().Be("XYZ Corporation");
    }

    [Fact]
    public async Task SearchSuppliersAsync_SearchByPartialName_ReturnsMatchingSuppliers()
    {
        // Arrange
        _mockContactListClient
            .Setup(x => x.GetAsync(It.Is<ContactType>(ct => ct == ContactType.Supplier), It.Is<int>(i => i == 0), It.Is<int>(i => i == 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testContacts);

        // Act - Search with partial name
        var result = await _repository.SearchSuppliersAsync("Company", 10, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("ABC Company Ltd");
    }

    [Fact]
    public async Task SearchSuppliersAsync_WithLimit_ReturnsLimitedResults()
    {
        // Arrange
        _mockContactListClient
            .Setup(x => x.GetAsync(It.Is<ContactType>(ct => ct == ContactType.Supplier), It.Is<int>(i => i == 0), It.Is<int>(i => i == 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testContacts);

        // Act
        var result = await _repository.SearchSuppliersAsync("", 2, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchSuppliersAsync_EmptySearchTerm_ReturnsAllSuppliers()
    {
        // Arrange
        _mockContactListClient
            .Setup(x => x.GetAsync(It.Is<ContactType>(ct => ct == ContactType.Supplier), It.Is<int>(i => i == 0), It.Is<int>(i => i == 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testContacts);

        // Act
        var result = await _repository.SearchSuppliersAsync("", 10, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain(s => s.Name == "ABC Company Ltd");
        result.Should().Contain(s => s.Name == "XYZ Corporation");
        result.Should().Contain(s => s.Name == "DEF Industries");
    }

    [Fact]
    public async Task GetByIdAsync_CacheMiss_LoadsFromContactListClient()
    {
        // Arrange
        _mockContactListClient
            .Setup(x => x.GetAsync(It.Is<ContactType>(ct => ct == ContactType.Supplier), It.Is<int>(i => i == 0), It.Is<int>(i => i == 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testContacts);

        // Act
        var result = await _repository.GetByIdAsync(1, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("ABC Company Ltd");
        result.Code.Should().Be("ABC001");
        result.Email.Should().Be("abc@example.com");
        result.Phone.Should().Be("+420123456789");
        result.Url.Should().Be("https://abc.com");
        result.Note.Should().Be("Test supplier 1");

        _mockContactListClient.Verify(
            x => x.GetAsync(It.Is<ContactType>(ct => ct == ContactType.Supplier), It.Is<int>(i => i == 0), It.Is<int>(i => i == 0), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_CacheHit_DoesNotCallContactListClient()
    {
        // Arrange - First call to populate cache
        _mockContactListClient
            .Setup(x => x.GetAsync(It.Is<ContactType>(ct => ct == ContactType.Supplier), It.Is<int>(i => i == 0), It.Is<int>(i => i == 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testContacts);

        await _repository.GetByIdAsync(1, CancellationToken.None);

        // Act - Second call should use cache
        var result = await _repository.GetByIdAsync(2, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(2);
        result.Name.Should().Be("XYZ Corporation");

        _mockContactListClient.Verify(
            x => x.GetAsync(It.Is<ContactType>(ct => ct == ContactType.Supplier), It.Is<int>(i => i == 0), It.Is<int>(i => i == 0), It.IsAny<CancellationToken>()),
            Times.Once); // Only called once
    }

    [Fact]
    public async Task GetByIdAsync_SupplierNotFound_ReturnsNull()
    {
        // Arrange
        _mockContactListClient
            .Setup(x => x.GetAsync(It.Is<ContactType>(ct => ct == ContactType.Supplier), It.Is<int>(i => i == 0), It.Is<int>(i => i == 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testContacts);

        // Act
        var result = await _repository.GetByIdAsync(999, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SearchSuppliersAsync_ContactListClientThrowsException_PropagatesExceptionAndLogsError()
    {
        // Arrange
        var exception = new InvalidOperationException("FlexiBee connection failed");
        _mockContactListClient
            .Setup(x => x.GetAsync(It.IsAny<ContactType>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act & Assert
        var act = async () => await _repository.SearchSuppliersAsync("ABC", 10, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("FlexiBee connection failed");

        _mockAuditService.Verify(
            x => x.LogDataLoadAsync(
                "All Suppliers Load",
                "Flexi ERP",
                0,
                false,
                It.IsAny<Dictionary<string, object>>(),
                "FlexiBee connection failed",
                It.IsAny<TimeSpan>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error loading all suppliers")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task MapToSupplier_HandlesNullValues_CreatesValidSupplier()
    {
        // Arrange
        var contactWithNulls = new List<ContactFlexiDto>
        {
            new ContactFlexiDto
            {
                Id = null,
                Name = null!,
                Code = null!,
                Note = null,
                Email = null,
                Phone = null,
                Website = null
            }
        };

        _mockContactListClient
            .Setup(x => x.GetAsync(It.IsAny<ContactType>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contactWithNulls);

        // Act
        var result = await _repository.SearchSuppliersAsync("", 10, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        var supplier = result[0];
        supplier.Id.Should().Be(0L);
        supplier.Name.Should().Be(string.Empty);
        supplier.Code.Should().Be(string.Empty);
        supplier.Note.Should().BeNull();
        supplier.Email.Should().BeNull();
        supplier.Phone.Should().BeNull();
        supplier.Url.Should().BeNull();
    }

    [Theory]
    [InlineData("ABC", 1)]
    [InlineData("abc", 1)]
    [InlineData("ABC001", 1)]
    [InlineData("abc001", 1)]
    [InlineData("Company", 1)]
    [InlineData("company", 1)]
    [InlineData("XYZ", 1)]
    [InlineData("Corporation", 1)]
    [InlineData("DEF", 1)]
    [InlineData("Industries", 1)]
    [InlineData("NonExistent", 0)]
    [InlineData("", 3)]
    public async Task SearchSuppliersAsync_VariousSearchTerms_ReturnsExpectedCount(string searchTerm, int expectedCount)
    {
        // Arrange
        _mockContactListClient
            .Setup(x => x.GetAsync(It.Is<ContactType>(ct => ct == ContactType.Supplier), It.Is<int>(i => i == 0), It.Is<int>(i => i == 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testContacts);

        // Act
        var result = await _repository.SearchSuppliersAsync(searchTerm, 10, CancellationToken.None);

        // Assert
        result.Should().HaveCount(expectedCount);
    }
}