using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.CreateManufactureDifficulty;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class CreateManufactureDifficultyHandlerTests
{
    private readonly Mock<IManufactureDifficultyRepository> _repositoryMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<CreateManufactureDifficultyHandler>> _loggerMock;
    private readonly CreateManufactureDifficultyHandler _handler;

    public CreateManufactureDifficultyHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureDifficultyRepository>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<CreateManufactureDifficultyHandler>>();

        // Setup default current user behavior
        _currentUserServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", "Test User", "test@example.com", true));

        _handler = new CreateManufactureDifficultyHandler(_repositoryMock.Object, _catalogRepositoryMock.Object, _currentUserServiceMock.Object, _mapperMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidFromAfterValidTo_ThrowsArgumentException()
    {
        // Arrange
        var request = new CreateManufactureDifficultyRequest
        {
            ProductCode = "PROD001",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 10),
            ValidTo = new DateTime(2024, 1, 5) // ValidTo before ValidFrom
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.Handle(request, CancellationToken.None));

        Assert.Equal("ValidFrom must be earlier than ValidTo", exception.Message);
    }

    [Fact]
    public async Task Handle_NoOverlaps_CreatesNewSettingDirectly()
    {
        // Arrange
        var request = new CreateManufactureDifficultyRequest
        {
            ProductCode = "PROD001",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        var mappedSetting = new ManufactureDifficultySetting
        {
            ProductCode = "PROD001",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        var createdSetting = new ManufactureDifficultySetting
        {
            Id = 1,
            ProductCode = "PROD001",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "Test User"
        };

        var expectedDto = new ManufactureDifficultySettingDto
        {
            Id = 1,
            ProductCode = "PROD001",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        _repositoryMock.Setup(r => r.ListAsync("PROD001", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureDifficultySetting>());

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySetting>(request))
            .Returns(mappedSetting);

        _repositoryMock.Setup(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdSetting);

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySettingDto>(createdSetting))
            .Returns(expectedDto);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedDto, result.DifficultyHistory);
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()), Times.Once);
        _catalogRepositoryMock.Verify(r => r.RefreshManufactureDifficultySettingsData("PROD001", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CompletelyOverlappedPeriod_RemovesExistingPeriod()
    {
        // Arrange
        var request = new CreateManufactureDifficultyRequest
        {
            ProductCode = "PROD001",
            DifficultyValue = 3,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        var existingSetting = new ManufactureDifficultySetting
        {
            Id = 1,
            ProductCode = "PROD001",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 6, 1),
            ValidTo = new DateTime(2024, 8, 31)
        };

        var mappedSetting = new ManufactureDifficultySetting
        {
            ProductCode = "PROD001",
            DifficultyValue = 3,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        var createdSetting = new ManufactureDifficultySetting
        {
            Id = 2,
            ProductCode = "PROD001",
            DifficultyValue = 3,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "Test User"
        };

        _repositoryMock.Setup(r => r.ListAsync("PROD001", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureDifficultySetting> { existingSetting });

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySetting>(request))
            .Returns(mappedSetting);

        _repositoryMock.Setup(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdSetting);

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySettingDto>(createdSetting))
            .Returns(new ManufactureDifficultySettingDto());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(r => r.DeleteAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _catalogRepositoryMock.Verify(r => r.RefreshManufactureDifficultySettingsData("PROD001", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingPeriodExtendsBeforeNew_AdjustsValidTo()
    {
        // Arrange
        var request = new CreateManufactureDifficultyRequest
        {
            ProductCode = "PROD001",
            DifficultyValue = 3,
            ValidFrom = new DateTime(2024, 6, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        var existingSetting = new ManufactureDifficultySetting
        {
            Id = 1,
            ProductCode = "PROD001",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 8, 31) // Overlaps with new period
        };

        _repositoryMock.Setup(r => r.ListAsync("PROD001", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureDifficultySetting> { existingSetting });

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySetting>(request))
            .Returns(new ManufactureDifficultySetting());

        _repositoryMock.Setup(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting());

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySettingDto>(It.IsAny<ManufactureDifficultySetting>()))
            .Returns(new ManufactureDifficultySettingDto());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.Equal(new DateTime(2024, 5, 31), existingSetting.ValidTo); // Should be set to day before new ValidFrom
        _repositoryMock.Verify(r => r.UpdateAsync(existingSetting, It.IsAny<CancellationToken>()), Times.Once);
        _catalogRepositoryMock.Verify(r => r.RefreshManufactureDifficultySettingsData("PROD001", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingPeriodExtendsAfterNew_AdjustsValidFrom()
    {
        // Arrange
        var request = new CreateManufactureDifficultyRequest
        {
            ProductCode = "PROD001",
            DifficultyValue = 3,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 6, 30)
        };

        var existingSetting = new ManufactureDifficultySetting
        {
            Id = 1,
            ProductCode = "PROD001",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 4, 1),
            ValidTo = new DateTime(2024, 12, 31) // Overlaps with new period
        };

        _repositoryMock.Setup(r => r.ListAsync("PROD001", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureDifficultySetting> { existingSetting });

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySetting>(request))
            .Returns(new ManufactureDifficultySetting());

        _repositoryMock.Setup(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting());

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySettingDto>(It.IsAny<ManufactureDifficultySetting>()))
            .Returns(new ManufactureDifficultySettingDto());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.Equal(new DateTime(2024, 7, 1), existingSetting.ValidFrom); // Should be set to day after new ValidTo
        _repositoryMock.Verify(r => r.UpdateAsync(existingSetting, It.IsAny<CancellationToken>()), Times.Once);
        _catalogRepositoryMock.Verify(r => r.RefreshManufactureDifficultySettingsData("PROD001", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnlimitedValidToPeriod_AdjustsToOneDayBeforeNewPeriod()
    {
        // Arrange - The most common scenario mentioned by user
        var request = new CreateManufactureDifficultyRequest
        {
            ProductCode = "PROD001",
            DifficultyValue = 3,
            ValidFrom = DateTime.Today, // Today
            ValidTo = null
        };

        var existingSetting = new ManufactureDifficultySetting
        {
            Id = 1,
            ProductCode = "PROD001",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2023, 1, 1),
            ValidTo = null // Unlimited validity
        };

        _repositoryMock.Setup(r => r.ListAsync("PROD001", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureDifficultySetting> { existingSetting });

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySetting>(request))
            .Returns(new ManufactureDifficultySetting());

        _repositoryMock.Setup(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting());

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySettingDto>(It.IsAny<ManufactureDifficultySetting>()))
            .Returns(new ManufactureDifficultySettingDto());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.Equal(DateTime.Today.AddDays(-1), existingSetting.ValidTo); // Should be set to yesterday
        _repositoryMock.Verify(r => r.UpdateAsync(existingSetting, It.IsAny<CancellationToken>()), Times.Once);
        _catalogRepositoryMock.Verify(r => r.RefreshManufactureDifficultySettingsData("PROD001", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MultipleOverlappingPeriods_ResolvesAllConflicts()
    {
        // Arrange
        var request = new CreateManufactureDifficultyRequest
        {
            ProductCode = "PROD001",
            DifficultyValue = 4,
            ValidFrom = new DateTime(2024, 6, 1),
            ValidTo = new DateTime(2024, 8, 31)
        };

        var existingSettings = new List<ManufactureDifficultySetting>
        {
            new ManufactureDifficultySetting // Should be adjusted ValidTo
            {
                Id = 1,
                ProductCode = "PROD001",
                DifficultyValue = 2,
                ValidFrom = new DateTime(2024, 1, 1),
                ValidTo = new DateTime(2024, 7, 15) // Overlaps
            },
            new ManufactureDifficultySetting // Should be completely removed
            {
                Id = 2,
                ProductCode = "PROD001",
                DifficultyValue = 3,
                ValidFrom = new DateTime(2024, 6, 15),
                ValidTo = new DateTime(2024, 7, 31) // Completely covered
            },
            new ManufactureDifficultySetting // Should be adjusted ValidFrom
            {
                Id = 3,
                ProductCode = "PROD001",
                DifficultyValue = 1,
                ValidFrom = new DateTime(2024, 7, 1),
                ValidTo = new DateTime(2024, 12, 31) // Overlaps
            }
        };

        _repositoryMock.Setup(r => r.ListAsync("PROD001", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSettings);

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySetting>(request))
            .Returns(new ManufactureDifficultySetting());

        _repositoryMock.Setup(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting());

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySettingDto>(It.IsAny<ManufactureDifficultySetting>()))
            .Returns(new ManufactureDifficultySettingDto());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.Equal(new DateTime(2024, 5, 31), existingSettings[0].ValidTo); // Adjusted
        Assert.Equal(new DateTime(2024, 9, 1), existingSettings[2].ValidFrom); // Adjusted

        _repositoryMock.Verify(r => r.DeleteAsync(2, It.IsAny<CancellationToken>()), Times.Once); // Completely overlapped removed
        _repositoryMock.Verify(r => r.UpdateAsync(existingSettings[0], It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.UpdateAsync(existingSettings[2], It.IsAny<CancellationToken>()), Times.Once);
        _catalogRepositoryMock.Verify(r => r.RefreshManufactureDifficultySettingsData("PROD001", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NewPeriodWithoutValidFrom_HandlesNullValidFromCorrectly()
    {
        // Arrange
        var request = new CreateManufactureDifficultyRequest
        {
            ProductCode = "PROD001",
            DifficultyValue = 3,
            ValidFrom = null, // Unlimited start
            ValidTo = new DateTime(2024, 6, 30)
        };

        var existingSetting = new ManufactureDifficultySetting
        {
            Id = 1,
            ProductCode = "PROD001",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31) // Should be adjusted ValidFrom
        };

        _repositoryMock.Setup(r => r.ListAsync("PROD001", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureDifficultySetting> { existingSetting });

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySetting>(request))
            .Returns(new ManufactureDifficultySetting());

        _repositoryMock.Setup(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting());

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySettingDto>(It.IsAny<ManufactureDifficultySetting>()))
            .Returns(new ManufactureDifficultySettingDto());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.Equal(new DateTime(2024, 7, 1), existingSetting.ValidFrom); // Should be set to day after new ValidTo
        _repositoryMock.Verify(r => r.UpdateAsync(existingSetting, It.IsAny<CancellationToken>()), Times.Once);
        _catalogRepositoryMock.Verify(r => r.RefreshManufactureDifficultySettingsData("PROD001", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NewPeriodWithoutValidTo_HandlesNullValidToCorrectly()
    {
        // Arrange
        var request = new CreateManufactureDifficultyRequest
        {
            ProductCode = "PROD001",
            DifficultyValue = 3,
            ValidFrom = new DateTime(2024, 6, 1),
            ValidTo = null // Unlimited end
        };

        var existingSetting = new ManufactureDifficultySetting
        {
            Id = 1,
            ProductCode = "PROD001",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31) // Should be adjusted ValidTo
        };

        _repositoryMock.Setup(r => r.ListAsync("PROD001", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureDifficultySetting> { existingSetting });

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySetting>(request))
            .Returns(new ManufactureDifficultySetting());

        _repositoryMock.Setup(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManufactureDifficultySetting());

        _mapperMock.Setup(m => m.Map<ManufactureDifficultySettingDto>(It.IsAny<ManufactureDifficultySetting>()))
            .Returns(new ManufactureDifficultySettingDto());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.Equal(new DateTime(2024, 5, 31), existingSetting.ValidTo); // Should be set to day before new ValidFrom
        _repositoryMock.Verify(r => r.UpdateAsync(existingSetting, It.IsAny<CancellationToken>()), Times.Once);
        _catalogRepositoryMock.Verify(r => r.RefreshManufactureDifficultySettingsData("PROD001", It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}