using Anela.Heblo.Application.Features.Bank;
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementById;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class GetBankStatementByIdHandlerTests
{
    private readonly Mock<IBankStatementImportRepository> _repository;
    private readonly IMapper _mapper;
    private readonly Mock<ILogger<GetBankStatementByIdHandler>> _logger;
    private readonly GetBankStatementByIdHandler _handler;

    public GetBankStatementByIdHandlerTests()
    {
        _repository = new Mock<IBankStatementImportRepository>();
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<BankMappingProfile>(), NullLoggerFactory.Instance);
        _mapper = mapperConfig.CreateMapper();
        _logger = new Mock<ILogger<GetBankStatementByIdHandler>>();
        _handler = new GetBankStatementByIdHandler(_repository.Object, _mapper, _logger.Object);
    }

    [Fact]
    public async Task Handle_WithExistingId_ReturnsMappedDto()
    {
        // Arrange
        var entity = new BankStatementImport("T12345", new DateTime(2026, 1, 15))
        {
            Account = "123456789",
            Currency = CurrencyCode.CZK,
            ItemCount = 7,
            ImportResult = "OK"
        };
        _repository
            .Setup(r => r.GetByIdAsync(42))
            .ReturnsAsync(entity);

        // Act
        var result = await _handler.Handle(new GetBankStatementByIdRequest { Id = 42 }, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("T12345", result!.TransferId);
        Assert.Equal("123456789", result.Account);
        Assert.Equal("CZK", result.Currency);
        Assert.Equal(7, result.ItemCount);
        Assert.Equal("OK", result.ImportResult);
        Assert.Null(result.ErrorType);
    }

    [Fact]
    public async Task Handle_WithMissingId_ReturnsNull()
    {
        // Arrange
        _repository
            .Setup(r => r.GetByIdAsync(99999))
            .ReturnsAsync((BankStatementImport?)null);

        // Act
        var result = await _handler.Handle(new GetBankStatementByIdRequest { Id = 99999 }, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_CallsRepositoryGetByIdExactlyOnce_WithTheRequestId()
    {
        // Arrange
        _repository
            .Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((BankStatementImport?)null);

        // Act
        await _handler.Handle(new GetBankStatementByIdRequest { Id = 123 }, CancellationToken.None);

        // Assert
        _repository.Verify(r => r.GetByIdAsync(123), Times.Once);
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_ProducesSameDtoAsListHandlerMapping_ForSameEntity()
    {
        // Arrange — guarantees no projection drift between list and by-id paths.
        var entity = new BankStatementImport("T-SAMENESS", new DateTime(2026, 2, 1))
        {
            Account = "987654321",
            Currency = CurrencyCode.EUR,
            ItemCount = 3,
            ImportResult = "PROCESSING_ERROR"
        };
        _repository.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(entity);

        // Act
        var fromHandler = await _handler.Handle(new GetBankStatementByIdRequest { Id = 7 }, CancellationToken.None);
        var fromListMapping = _mapper.Map<List<BankStatementImportDto>>(new[] { entity }).Single();

        // Assert
        Assert.NotNull(fromHandler);
        Assert.Equal(fromListMapping.Id, fromHandler!.Id);
        Assert.Equal(fromListMapping.TransferId, fromHandler.TransferId);
        Assert.Equal(fromListMapping.StatementDate, fromHandler.StatementDate);
        Assert.Equal(fromListMapping.ImportDate, fromHandler.ImportDate);
        Assert.Equal(fromListMapping.Account, fromHandler.Account);
        Assert.Equal(fromListMapping.Currency, fromHandler.Currency);
        Assert.Equal(fromListMapping.ItemCount, fromHandler.ItemCount);
        Assert.Equal(fromListMapping.ImportResult, fromHandler.ImportResult);
        Assert.Equal(fromListMapping.ErrorType, fromHandler.ErrorType);
    }
}
