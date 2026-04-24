using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.UseCases.AddRoot;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class AddRootHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repoMock = new();

    private AddRootHandler CreateHandler() => new(_repoMock.Object);

    [Fact]
    public async Task Handle_PersistsDriveIdAndRootItemId()
    {
        // Arrange
        PhotobankIndexRoot? savedRoot = null;
        _repoMock
            .Setup(r => r.AddRootAsync(It.IsAny<PhotobankIndexRoot>(), It.IsAny<CancellationToken>()))
            .Callback<PhotobankIndexRoot, CancellationToken>((root, _) => savedRoot = root)
            .ReturnsAsync((PhotobankIndexRoot root, CancellationToken _) =>
            {
                root.Id = 42;
                return root;
            });

        var request = new AddRootRequest
        {
            SharePointPath = "/Fotky/Produkty",
            DisplayName = "Produkty",
            DriveId = "drive-abc",
            RootItemId = "item-xyz",
        };

        // Act
        var response = await CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.Id.Should().Be(42);
        savedRoot.Should().NotBeNull();
        savedRoot!.DriveId.Should().Be("drive-abc");
        savedRoot.RootItemId.Should().Be("item-xyz");
        savedRoot.SharePointPath.Should().Be("/Fotky/Produkty");
        savedRoot.DisplayName.Should().Be("Produkty");
        savedRoot.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_TrimsWhitespaceFromStringFields()
    {
        // Arrange
        PhotobankIndexRoot? savedRoot = null;
        _repoMock
            .Setup(r => r.AddRootAsync(It.IsAny<PhotobankIndexRoot>(), It.IsAny<CancellationToken>()))
            .Callback<PhotobankIndexRoot, CancellationToken>((root, _) => savedRoot = root)
            .ReturnsAsync((PhotobankIndexRoot root, CancellationToken _) => root);

        var request = new AddRootRequest
        {
            SharePointPath = "  /Fotky  ",
            DisplayName = "  Název  ",
            DriveId = "  drive-abc  ",
            RootItemId = "  item-xyz  ",
        };

        // Act
        await CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        savedRoot!.SharePointPath.Should().Be("/Fotky");
        savedRoot.DisplayName.Should().Be("Název");
        savedRoot.DriveId.Should().Be("drive-abc");
        savedRoot.RootItemId.Should().Be("item-xyz");
    }

    [Fact]
    public async Task Handle_CallsSaveChanges()
    {
        // Arrange
        _repoMock
            .Setup(r => r.AddRootAsync(It.IsAny<PhotobankIndexRoot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PhotobankIndexRoot root, CancellationToken _) => root);

        var request = new AddRootRequest
        {
            SharePointPath = "/Fotky",
            DriveId = "drive-1",
            RootItemId = "item-1",
        };

        // Act
        await CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
