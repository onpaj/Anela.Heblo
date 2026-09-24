using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingBaseline;
using Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingScenario;
using Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingScenarios;
using Anela.Heblo.Application.Features.Pricing.UseCases.RecalculatePricing;
using Anela.Heblo.Application.Features.Pricing.UseCases.SavePricingScenario;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Users;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class PricingSimulatorMcpToolsTests
{
    private static readonly string ReadRole = AccessRoles.For(Feature.Finance_PriceAnalysis, AccessLevel.Read);
    private static readonly string WriteRole = AccessRoles.For(Feature.Finance_PriceAnalysis, AccessLevel.Write);

    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly PricingSimulatorMcpTools _tools;

    public PricingSimulatorMcpToolsTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _currentUserServiceMock.Setup(s => s.IsInRole(ReadRole)).Returns(true);
        _tools = new PricingSimulatorMcpTools(_mediatorMock.Object, _currentUserServiceMock.Object);
    }

    private static PricingRowDto Row(string code, bool isEdited = false) =>
        new() { ProductCode = code, IsEdited = isEdited };

    [Fact]
    public async Task GetPricingBaseline_MapsFiltersAndReturnsRows()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetPricingBaselineRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPricingBaselineResponse { Rows = new() { Row("AKL001") } });

        // Act
        var json = await _tools.GetPricingBaseline("AKL", "krém", ProductType.Product);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetPricingBaselineRequest>(r =>
                r.ProductCode == "AKL" && r.ProductName == "krém" && r.ProductType == ProductType.Product),
            It.IsAny<CancellationToken>()), Times.Once);

        var result = JsonSerializer.Deserialize<GetPricingBaselineResponse>(json, McpJsonOptions.Default);
        Assert.Single(result!.Rows);
    }

    [Fact]
    public async Task GetPricingBaseline_Throws_WhenUserLacksReadAccess()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.IsInRole(ReadRole)).Returns(false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<McpException>(() => _tools.GetPricingBaseline());
        Assert.Contains("FORBIDDEN", ex.Message);
        _mediatorMock.Verify(m => m.Send(It.IsAny<GetPricingBaselineRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public static TheoryData<string> ReadGatedTools => new()
    {
        nameof(PricingSimulatorMcpTools.SimulatePricing),
        nameof(PricingSimulatorMcpTools.ListPricingScenarios),
        nameof(PricingSimulatorMcpTools.GetPricingScenario),
    };

    [Theory]
    [MemberData(nameof(ReadGatedTools))]
    public async Task ReadTools_Throw_WhenUserLacksReadAccess(string toolName)
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.IsInRole(ReadRole)).Returns(false);
        Func<Task<string>> call = toolName switch
        {
            nameof(PricingSimulatorMcpTools.SimulatePricing) => () => _tools.SimulatePricing(),
            nameof(PricingSimulatorMcpTools.ListPricingScenarios) => () => _tools.ListPricingScenarios(),
            _ => () => _tools.GetPricingScenario(Guid.NewGuid()),
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<McpException>(call);
        Assert.Contains("FORBIDDEN", ex.Message);
        _mediatorMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SimulatePricing_WithoutEdits_ReplaysOverridesOnce()
    {
        // Arrange
        var overrides = new List<PricingOverrideDto> { new() { ProductCode = "AKL001", Price = 120m } };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RecalculatePricingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecalculatePricingResponse { Overrides = overrides });

        // Act
        await _tools.SimulatePricing(overrides: overrides, productType: ProductType.Goods);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<RecalculatePricingRequest>(r =>
                r.Edit == null && r.Overrides.Count == 1 && r.ProductType == ProductType.Goods),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SimulatePricing_AppliesEditsInOrder_ChainingOverrides()
    {
        // Arrange
        var afterFirst = new List<PricingOverrideDto> { new() { ProductCode = "AKL001", Price = 150m } };
        var afterSecond = new List<PricingOverrideDto>
        {
            new() { ProductCode = "AKL001", Price = 150m },
            new() { ProductCode = "AKL002", MaterialCost = 40m }
        };
        var edits = new List<PricingEditDto>
        {
            new() { ProductCode = "AKL001", Field = PricingEditField.Price, Value = 150m },
            new() { ProductCode = "AKL002", Field = PricingEditField.M0Percentage, Value = 60m }
        };

        _mediatorMock
            .Setup(m => m.Send(It.Is<RecalculatePricingRequest>(r => r.Edit!.ProductCode == "AKL001"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecalculatePricingResponse { Overrides = afterFirst });
        _mediatorMock
            .Setup(m => m.Send(It.Is<RecalculatePricingRequest>(r => r.Edit!.ProductCode == "AKL002"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecalculatePricingResponse
            {
                Overrides = afterSecond,
                Rows = new() { Row("AKL001", isEdited: true), Row("AKL002", isEdited: true) }
            });

        // Act
        var json = await _tools.SimulatePricing(edits: edits);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<RecalculatePricingRequest>(r => r.Edit!.ProductCode == "AKL002" && r.Overrides == afterFirst),
            It.IsAny<CancellationToken>()), Times.Once);

        var result = JsonSerializer.Deserialize<RecalculatePricingResponse>(json, McpJsonOptions.Default);
        Assert.Equal(2, result!.Overrides.Count);
    }

    [Fact]
    public async Task SimulatePricing_ReturnsOnlyEditedRows_ByDefault()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RecalculatePricingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecalculatePricingResponse { Rows = new() { Row("AKL001", isEdited: true), Row("AKL002") } });

        // Act
        var json = await _tools.SimulatePricing();

        // Assert
        var result = JsonSerializer.Deserialize<RecalculatePricingResponse>(json, McpJsonOptions.Default);
        Assert.Equal("AKL001", Assert.Single(result!.Rows).ProductCode);
    }

    [Fact]
    public async Task SimulatePricing_ReturnsAllRows_WhenOnlyEditedRowsIsFalse()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RecalculatePricingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecalculatePricingResponse { Rows = new() { Row("AKL001", isEdited: true), Row("AKL002") } });

        // Act
        var json = await _tools.SimulatePricing(onlyEditedRows: false);

        // Assert
        var result = JsonSerializer.Deserialize<RecalculatePricingResponse>(json, McpJsonOptions.Default);
        Assert.Equal(2, result!.Rows.Count);
    }

    [Fact]
    public async Task SimulatePricing_Throws_WhenAnEditIsImpossible()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RecalculatePricingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecalculatePricingResponse(
                ErrorCodes.PricingNegativeMaterialCost,
                new Dictionary<string, string> { { "productCode", "AKL001" } }));
        var edits = new List<PricingEditDto>
        {
            new() { ProductCode = "AKL001", Field = PricingEditField.M0Percentage, Value = 150m }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<McpException>(() => _tools.SimulatePricing(edits: edits));
        Assert.Contains(nameof(ErrorCodes.PricingNegativeMaterialCost), ex.Message);
    }

    [Fact]
    public async Task SimulatePricing_NamesTheFailingEdit_WhenALaterEditInTheBatchFails()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.Is<RecalculatePricingRequest>(r => r.Edit!.ProductCode == "AKL001"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecalculatePricingResponse
            {
                Overrides = new() { new() { ProductCode = "AKL001", Price = 150m } }
            });
        _mediatorMock
            .Setup(m => m.Send(It.Is<RecalculatePricingRequest>(r => r.Edit!.ProductCode == "AKL002"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecalculatePricingResponse(
                ErrorCodes.PricingNegativeMaterialCost, new Dictionary<string, string>()));
        var edits = new List<PricingEditDto>
        {
            new() { ProductCode = "AKL001", Field = PricingEditField.Price, Value = 150m },
            new() { ProductCode = "AKL002", Field = PricingEditField.M0Percentage, Value = 150m }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<McpException>(() => _tools.SimulatePricing(edits: edits));
        Assert.Contains("Edit #2 of 2", ex.Message);
        Assert.Contains("AKL002 M0Percentage", ex.Message);
        Assert.Contains("no edits were applied", ex.Message);
        Assert.Contains(nameof(ErrorCodes.PricingNegativeMaterialCost), ex.Message);
    }

    [Fact]
    public async Task SimulatePricing_TranslatesValidationException_ToMcpException()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RecalculatePricingRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException(new[] { new ValidationFailure("Overrides[0].Price", "must be > 0") }));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<McpException>(() => _tools.SimulatePricing());
        Assert.Contains("Overrides[0].Price: must be > 0", ex.Message);
    }

    [Fact]
    public async Task ListPricingScenarios_ReturnsScenarios()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetPricingScenariosRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPricingScenariosResponse { Scenarios = new() { new() { Name = "Zdražení 2026" } } });

        // Act
        var json = await _tools.ListPricingScenarios();

        // Assert
        var result = JsonSerializer.Deserialize<GetPricingScenariosResponse>(json, McpJsonOptions.Default);
        Assert.Equal("Zdražení 2026", Assert.Single(result!.Scenarios).Name);
    }

    [Fact]
    public async Task GetPricingScenario_ReturnsOnlyEditedRows_ByDefault()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mediatorMock
            .Setup(m => m.Send(It.Is<GetPricingScenarioRequest>(r => r.Id == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPricingScenarioResponse { Rows = new() { Row("AKL001", isEdited: true), Row("AKL002") } });

        // Act
        var json = await _tools.GetPricingScenario(id);

        // Assert
        var result = JsonSerializer.Deserialize<GetPricingScenarioResponse>(json, McpJsonOptions.Default);
        Assert.Single(result!.Rows);
    }

    [Fact]
    public async Task GetPricingScenario_ReturnsAllRows_WhenOnlyEditedRowsIsFalse()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mediatorMock
            .Setup(m => m.Send(It.Is<GetPricingScenarioRequest>(r => r.Id == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPricingScenarioResponse { Rows = new() { Row("AKL001", isEdited: true), Row("AKL002") } });

        // Act
        var json = await _tools.GetPricingScenario(id, onlyEditedRows: false);

        // Assert
        var result = JsonSerializer.Deserialize<GetPricingScenarioResponse>(json, McpJsonOptions.Default);
        Assert.Equal(2, result!.Rows.Count);
    }

    [Fact]
    public async Task GetPricingScenario_Throws_WhenNotFound()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetPricingScenarioRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPricingScenarioResponse(ErrorCodes.PricingScenarioNotFound, new Dictionary<string, string>()));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<McpException>(() => _tools.GetPricingScenario(Guid.NewGuid()));
        Assert.Contains(nameof(ErrorCodes.PricingScenarioNotFound), ex.Message);
    }

    [Fact]
    public async Task SavePricingScenario_Throws_WhenUserLacksWriteAccess()
    {
        // Arrange -- read access only

        // Act & Assert
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            _tools.SavePricingScenario("Zdražení", new List<PricingOverrideDto>()));
        Assert.Contains("FORBIDDEN", ex.Message);
        _mediatorMock.Verify(m => m.Send(It.IsAny<SavePricingScenarioRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SavePricingScenario_MapsParametersAndReturnsId()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.IsInRole(WriteRole)).Returns(true);
        var id = Guid.NewGuid();
        var overrides = new List<PricingOverrideDto> { new() { ProductCode = "AKL001", Price = 120m } };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<SavePricingScenarioRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SavePricingScenarioResponse { Id = id });

        // Act
        var json = await _tools.SavePricingScenario(
            "Zdražení", overrides, id: id, description: "popis", productType: ProductType.Product);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<SavePricingScenarioRequest>(r =>
                r.Id == id && r.Name == "Zdražení" && r.Description == "popis" &&
                r.ProductType == ProductType.Product && r.Overrides == overrides),
            It.IsAny<CancellationToken>()), Times.Once);

        var result = JsonSerializer.Deserialize<SavePricingScenarioResponse>(json, McpJsonOptions.Default);
        Assert.Equal(id, result!.Id);
    }
}
