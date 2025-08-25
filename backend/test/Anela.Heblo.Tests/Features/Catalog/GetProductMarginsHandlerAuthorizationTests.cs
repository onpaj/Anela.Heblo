using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Model;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetProductMarginsHandlerAuthorizationTests
{
    private readonly Mock<ICatalogRepository> _mockCatalogRepository;
    private readonly Mock<ILedgerService> _mockLedgerService;
    private readonly Mock<SafeMarginCalculator> _mockMarginCalculator;
    private readonly Mock<IAuthorizationService> _mockAuthorizationService;
    private readonly Mock<ICurrentUserService> _mockCurrentUserService;
    private readonly Mock<ILogger<GetProductMarginsHandler>> _mockLogger;
    private readonly GetProductMarginsHandler _handler;

    public GetProductMarginsHandlerAuthorizationTests()
    {
        _mockCatalogRepository = new Mock<ICatalogRepository>();
        _mockLedgerService = new Mock<ILedgerService>();
        _mockMarginCalculator = new Mock<SafeMarginCalculator>();
        _mockAuthorizationService = new Mock<IAuthorizationService>();
        _mockCurrentUserService = new Mock<ICurrentUserService>();
        _mockLogger = new Mock<ILogger<GetProductMarginsHandler>>();

        _handler = new GetProductMarginsHandler(
            _mockCatalogRepository.Object,
            _mockLedgerService.Object,
            TimeProvider.System,
            _mockMarginCalculator.Object,
            _mockAuthorizationService.Object,
            _mockCurrentUserService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WithDetailedAccess_ReturnsAllDataFields()
    {
        // Arrange
        var request = new GetProductMarginsRequest { PageNumber = 1, PageSize = 10 };
        var testProduct = CreateTestProduct();
        var testUser = new CurrentUser("test-user", "Test User", "test@example.com", true);
        var testPrincipal = CreateTestPrincipal(withDetailedAccess: true);

        _mockCurrentUserService.Setup(x => x.GetCurrentUser()).Returns(testUser);
        _mockCurrentUserService.Setup(x => x.GetCurrentPrincipal()).Returns(testPrincipal);
        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { testProduct });
        _mockAuthorizationService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), "ViewDetailedMargins"))
            .ReturnsAsync(AuthorizationResult.Success());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Items);
        var item = result.Items.First();
        
        // Should have all sensitive data visible
        Assert.NotNull(item.PriceWithoutVat);
        Assert.NotNull(item.PurchasePrice); // Sensitive data should be visible
        Assert.NotNull(item.AverageMaterialCost); // Sensitive data should be visible
        Assert.NotNull(item.AverageHandlingCost); // Sensitive data should be visible
    }

    [Fact]
    public async Task Handle_WithBasicAccessOnly_HidesSensitiveData()
    {
        // Arrange
        var request = new GetProductMarginsRequest { PageNumber = 1, PageSize = 10 };
        var testProduct = CreateTestProduct();
        var testUser = new CurrentUser("test-user", "Test User", "test@example.com", true);
        var testPrincipal = CreateTestPrincipal(withDetailedAccess: false);

        _mockCurrentUserService.Setup(x => x.GetCurrentUser()).Returns(testUser);
        _mockCurrentUserService.Setup(x => x.GetCurrentPrincipal()).Returns(testPrincipal);
        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { testProduct });
        _mockAuthorizationService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), "ViewDetailedMargins"))
            .ReturnsAsync(AuthorizationResult.Failed());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Items);
        var item = result.Items.First();
        
        // Should have public data visible but sensitive data hidden
        Assert.NotNull(item.PriceWithoutVat); // Public price should be visible
        Assert.Null(item.PurchasePrice); // Sensitive cost data should be hidden
        Assert.Null(item.AverageMaterialCost); // Sensitive cost data should be hidden
        Assert.Null(item.AverageHandlingCost); // Sensitive cost data should be hidden
    }

    [Fact]
    public async Task Handle_LogsUserAccess_ForAuditPurposes()
    {
        // Arrange
        var request = new GetProductMarginsRequest { PageNumber = 1, PageSize = 10 };
        var testProduct = CreateTestProduct();
        var testUser = new CurrentUser("audit-test-user", "Audit Test User", "audit@example.com", true);
        var testPrincipal = CreateTestPrincipal(withDetailedAccess: true);

        _mockCurrentUserService.Setup(x => x.GetCurrentUser()).Returns(testUser);
        _mockCurrentUserService.Setup(x => x.GetCurrentPrincipal()).Returns(testPrincipal);
        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { testProduct });
        _mockAuthorizationService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), "ViewDetailedMargins"))
            .ReturnsAsync(AuthorizationResult.Success());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        
        // Verify audit logging was called
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("audit-test-user") && v.ToString().Contains("accessing product margins data")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_LogsAuthorizationDecision_ForDebugging()
    {
        // Arrange
        var request = new GetProductMarginsRequest { PageNumber = 1, PageSize = 10 };
        var testProduct = CreateTestProduct();
        var testUser = new CurrentUser("debug-user", "Debug User", "debug@example.com", true);
        var testPrincipal = CreateTestPrincipal(withDetailedAccess: false);

        _mockCurrentUserService.Setup(x => x.GetCurrentUser()).Returns(testUser);
        _mockCurrentUserService.Setup(x => x.GetCurrentPrincipal()).Returns(testPrincipal);
        _mockCatalogRepository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { testProduct });
        _mockAuthorizationService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), "ViewDetailedMargins"))
            .ReturnsAsync(AuthorizationResult.Failed());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        
        // Verify authorization decision logging
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("User has detailed margins access: False")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    private ClaimsPrincipal CreateTestPrincipal(bool withDetailedAccess)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim("auth_scheme", "MockAuthentication")
        };

        if (withDetailedAccess)
        {
            claims.Add(new Claim("role", "FinancialManager"));
            claims.Add(new Claim("clearance", "confidential"));
        }
        else
        {
            claims.Add(new Claim("department", "finance"));
        }

        var identity = new ClaimsIdentity(claims, "Test");
        return new ClaimsPrincipal(identity);
    }

    private CatalogAggregate CreateTestProduct()
    {
        return new CatalogAggregate
        {
            ProductCode = "TEST001",
            ProductName = "Test Product",
            Type = ProductType.Product,
            EshopPrice = new EshopPrice { PriceWithoutVat = 100.00m },
            ErpPrice = new ErpPrice { PurchasePrice = 60.00m },
            ManufactureCostHistory = new List<ManufactureCost>
            {
                new ManufactureCost { MaterialCost = 30.00m, HandlingCost = 20.00m }
            },
            MarginPercentage = 40.0m,
            MarginAmount = 40.0m,
            ManufactureDifficulty = 2
        };
    }
}