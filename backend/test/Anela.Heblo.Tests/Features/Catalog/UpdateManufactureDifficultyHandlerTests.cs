using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.UpdateManufactureDifficulty;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class UpdateManufactureDifficultyHandlerTests
{
    private readonly Mock<IManufactureDifficultyRepository> _repositoryMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<ILogger<UpdateManufactureDifficultyHandler>> _loggerMock;
    private readonly UpdateManufactureDifficultyHandler _handler;

    public UpdateManufactureDifficultyHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureDifficultyRepository>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _mapperMock = new Mock<IMapper>();
        // TimeProvider is injected but unused by the handler; a bare mock is sufficient.
        _timeProviderMock = new Mock<TimeProvider>();
        _loggerMock = new Mock<ILogger<UpdateManufactureDifficultyHandler>>();

        _handler = new UpdateManufactureDifficultyHandler(
            _repositoryMock.Object,
            _catalogRepositoryMock.Object,
            _mapperMock.Object,
            _timeProviderMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_RecordNotFound_ReturnsManufactureDifficultyNotFoundAndPerformsNoFurtherWork()
    {
        // Arrange
        var request = new UpdateManufactureDifficultyRequest
        {
            Id = 42,
            DifficultyValue = 5,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureDifficultySetting?)null);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ManufactureDifficultyNotFound);
        response.Params.Should().ContainKey("id");
        response.Params!["id"].Should().Be("42");

        _repositoryMock.Verify(
            r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        _repositoryMock.Verify(
            r => r.HasOverlapAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _catalogRepositoryMock.Verify(
            r => r.RefreshManufactureDifficultySettingsData(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ValidFromEqualsValidTo_ReturnsInvalidValueAndPerformsNoFurtherWork()
    {
        // Arrange
        var sameDate = new DateTime(2024, 6, 15);
        var existing = new ManufactureDifficultySetting
        {
            Id = 1,
            ProductCode = "PROD001",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };
        var request = new UpdateManufactureDifficultyRequest
        {
            Id = 1,
            DifficultyValue = 5,
            ValidFrom = sameDate,
            ValidTo = sameDate
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
        response.Params.Should().ContainKey("field");
        response.Params!["field"].Should().Be("ValidFrom must be earlier than ValidTo");

        _repositoryMock.Verify(
            r => r.HasOverlapAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _catalogRepositoryMock.Verify(
            r => r.RefreshManufactureDifficultySettingsData(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ValidFromAfterValidTo_ReturnsInvalidValueAndPerformsNoFurtherWork()
    {
        // Arrange
        var existing = new ManufactureDifficultySetting
        {
            Id = 1,
            ProductCode = "PROD001",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };
        var request = new UpdateManufactureDifficultyRequest
        {
            Id = 1,
            DifficultyValue = 5,
            ValidFrom = new DateTime(2024, 7, 10),
            ValidTo = new DateTime(2024, 1, 1) // strictly before ValidFrom
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
        response.Params.Should().ContainKey("field");
        response.Params!["field"].Should().Be("ValidFrom must be earlier than ValidTo");

        _repositoryMock.Verify(
            r => r.HasOverlapAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _catalogRepositoryMock.Verify(
            r => r.RefreshManufactureDifficultySettingsData(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_BoundaryValidRange_ProceedsToOverlapCheck()
    {
        // Arrange
        var existing = new ManufactureDifficultySetting
        {
            Id = 1,
            ProductCode = "PROD001",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };
        // Smallest non-degenerate range: ValidFrom = ValidTo - 1 day
        var request = new UpdateManufactureDifficultyRequest
        {
            Id = 1,
            DifficultyValue = 5,
            ValidFrom = new DateTime(2024, 6, 14),
            ValidTo = new DateTime(2024, 6, 15)
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Force the next branch (overlap) to short-circuit so we don't need to set up the full happy path.
        _repositoryMock
            .Setup(r => r.HasOverlapAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        // Should NOT be InvalidValue — we passed the date-range guard.
        response.ErrorCode.Should().NotBe(ErrorCodes.InvalidValue);
        // Verifies the handler actually called HasOverlapAsync (proves we passed the guard).
        _repositoryMock.Verify(
            r => r.HasOverlapAsync(
                existing.ProductCode,
                request.ValidFrom,
                request.ValidTo,
                request.Id,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_OverlapDetected_ReturnsManufactureDifficultyConflictAndPerformsNoUpdate()
    {
        // Arrange
        var existing = new ManufactureDifficultySetting
        {
            Id = 7,
            ProductCode = "PROD-OVERLAP",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };
        var request = new UpdateManufactureDifficultyRequest
        {
            Id = 7,
            DifficultyValue = 9,
            ValidFrom = new DateTime(2024, 3, 1),
            ValidTo = new DateTime(2024, 9, 30)
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock
            .Setup(r => r.HasOverlapAsync(
                existing.ProductCode,
                request.ValidFrom,
                request.ValidTo,
                request.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ManufactureDifficultyConflict);
        response.Params.Should().ContainKey("productCode");
        response.Params!["productCode"].Should().Be("PROD-OVERLAP");

        _repositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _catalogRepositoryMock.Verify(
            r => r.RefreshManufactureDifficultySettingsData(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ValidUpdateNoOverlap_UpdatesEntityRefreshesCacheAndReturnsDto()
    {
        // Arrange
        var request = new UpdateManufactureDifficultyRequest
        {
            Id = 11,
            DifficultyValue = 8,
            ValidFrom = new DateTime(2024, 3, 1),
            ValidTo = new DateTime(2024, 6, 30)
        };

        var existing = new ManufactureDifficultySetting
        {
            Id = 11,
            ProductCode = "PROD-HAPPY",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31),
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedBy = "creator"
        };

        var updatedReturned = new ManufactureDifficultySetting
        {
            Id = 11,
            ProductCode = "PROD-HAPPY",
            DifficultyValue = 8,
            ValidFrom = new DateTime(2024, 3, 1),
            ValidTo = new DateTime(2024, 6, 30),
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedBy = "creator"
        };

        var expectedDto = new ManufactureDifficultySettingDto
        {
            Id = 11,
            ProductCode = "PROD-HAPPY",
            DifficultyValue = 8,
            ValidFrom = new DateTime(2024, 3, 1),
            ValidTo = new DateTime(2024, 6, 30)
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repositoryMock
            .Setup(r => r.HasOverlapAsync(
                existing.ProductCode,
                request.ValidFrom,
                request.ValidTo,
                request.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Mocked IMapper doesn't mutate by default; emulate the AutoMapper profile manually
        // so the entity passed to UpdateAsync carries the request's values.
        _mapperMock
            .Setup(m => m.Map(request, existing))
            .Callback<UpdateManufactureDifficultyRequest, ManufactureDifficultySetting>((req, target) =>
            {
                target.DifficultyValue = req.DifficultyValue;
                target.ValidFrom = req.ValidFrom;
                target.ValidTo = req.ValidTo;
            })
            .Returns(existing);

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<ManufactureDifficultySetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedReturned);

        _mapperMock
            .Setup(m => m.Map<ManufactureDifficultySettingDto>(updatedReturned))
            .Returns(expectedDto);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert — result contract
        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.DifficultyHistory.Should().BeSameAs(expectedDto);

        // Assert — repository orchestration
        _repositoryMock.Verify(
            r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()),
            Times.Once);

        // FR-7: explicit assertion that excludeId == request.Id was propagated
        _repositoryMock.Verify(
            r => r.HasOverlapAsync(
                existing.ProductCode,
                request.ValidFrom,
                request.ValidTo,
                It.Is<int?>(id => id == request.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Entity passed to UpdateAsync carries the request values (confirms the handler
        // passed `existing` to the mapper before persisting, not a different object).
        _repositoryMock.Verify(
            r => r.UpdateAsync(
                It.Is<ManufactureDifficultySetting>(s =>
                    s.Id == 11
                    && s.DifficultyValue == request.DifficultyValue
                    && s.ValidFrom == request.ValidFrom
                    && s.ValidTo == request.ValidTo),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Cache refresh fires with the product code of the existing entity.
        _catalogRepositoryMock.Verify(
            r => r.RefreshManufactureDifficultySettingsData("PROD-HAPPY", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
