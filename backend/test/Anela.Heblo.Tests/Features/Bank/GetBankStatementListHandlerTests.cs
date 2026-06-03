using Anela.Heblo.Application.Features.Bank;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;
using Anela.Heblo.Domain.Features.Bank;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class GetBankStatementListHandlerTests
{
    private readonly Mock<IBankStatementImportRepository> _repository = new();
    private readonly IMapper _mapper;
    private readonly GetBankStatementListHandler _handler;

    public GetBankStatementListHandlerTests()
    {
        var cfg = new MapperConfiguration(c =>
        {
            c.AddProfile<BankMappingProfile>();
        }, NullLoggerFactory.Instance);
        _mapper = cfg.CreateMapper();
        _handler = new GetBankStatementListHandler(_repository.Object, _mapper, NullLogger<GetBankStatementListHandler>.Instance);

        _repository
            .Setup(r => r.GetFilteredAsync(
                It.IsAny<BankStatementListFilter>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Enumerable.Empty<BankStatementImport>(), 0));
    }

    [Fact]
    public async Task Handle_PassesAllFilterFieldsToRepository()
    {
        // Arrange
        var request = new GetBankStatementListRequest
        {
            TransferId = "  ABC  ",
            Account = "  shoptet  ",
            DateFrom = "2026-01-01",
            DateTo = "2026-01-31",
            ErrorsOnly = true,
        };
        BankStatementListFilter? captured = null;
        _repository
            .Setup(r => r.GetFilteredAsync(
                It.IsAny<BankStatementListFilter>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<BankStatementListFilter, int, int, string, bool, CancellationToken>(
                (f, _, _, _, _, _) => captured = f)
            .ReturnsAsync((Enumerable.Empty<BankStatementImport>(), 0));

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.TransferId.Should().Be("ABC");                       // trimmed
        captured.Account.Should().Be("shoptet");                       // trimmed
        captured.DateFrom.Should().Be(new DateTime(2026, 1, 1));
        captured.DateTo.Should().Be(new DateTime(2026, 1, 31));
        captured.ErrorsOnly.Should().Be(true);
    }

    [Fact]
    public async Task Handle_IgnoresUnparseableDateStrings()
    {
        // Arrange
        var request = new GetBankStatementListRequest
        {
            DateFrom = "not-a-date",
            DateTo = "still-not-a-date",
        };
        BankStatementListFilter? captured = null;
        _repository
            .Setup(r => r.GetFilteredAsync(
                It.IsAny<BankStatementListFilter>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<BankStatementListFilter, int, int, string, bool, CancellationToken>(
                (f, _, _, _, _, _) => captured = f)
            .ReturnsAsync((Enumerable.Empty<BankStatementImport>(), 0));

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        captured!.DateFrom.Should().BeNull();
        captured.DateTo.Should().BeNull();
    }

    [Fact]
    public async Task Handle_OmitsEmptyOrWhitespaceStringFilters()
    {
        // Arrange
        var request = new GetBankStatementListRequest
        {
            TransferId = "   ",
            Account = "",
        };
        BankStatementListFilter? captured = null;
        _repository
            .Setup(r => r.GetFilteredAsync(
                It.IsAny<BankStatementListFilter>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<BankStatementListFilter, int, int, string, bool, CancellationToken>(
                (f, _, _, _, _, _) => captured = f)
            .ReturnsAsync((Enumerable.Empty<BankStatementImport>(), 0));

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        captured!.TransferId.Should().BeNull();
        captured.Account.Should().BeNull();
    }
}

public class GetBankStatementListRequestValidatorTests
{
    private readonly Anela.Heblo.Application.Features.Bank.Validators.GetBankStatementListRequestValidator _validator = new();

    [Fact]
    public void Validate_RejectsTransferIdLongerThan100Chars()
    {
        var request = new GetBankStatementListRequest { TransferId = new string('a', 101) };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.TransferId));
    }

    [Fact]
    public void Validate_RejectsAccountLongerThan100Chars()
    {
        var request = new GetBankStatementListRequest { Account = new string('a', 101) };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.Account));
    }

    [Fact]
    public void Validate_RejectsUnparseableDateFrom()
    {
        var request = new GetBankStatementListRequest { DateFrom = "not-a-date" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.DateFrom));
    }

    [Fact]
    public void Validate_RejectsUnparseableDateTo()
    {
        var request = new GetBankStatementListRequest { DateTo = "not-a-date" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.DateTo));
    }

    [Fact]
    public void Validate_RejectsDateFromLaterThanDateTo()
    {
        var request = new GetBankStatementListRequest { DateFrom = "2026-02-01", DateTo = "2026-01-01" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.DateFrom));
    }

    [Fact]
    public void Validate_AcceptsAllNullOptionalFields()
    {
        var request = new GetBankStatementListRequest { Take = 10, Skip = 0 };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AcceptsValidDateRange()
    {
        var request = new GetBankStatementListRequest { DateFrom = "2026-01-01", DateTo = "2026-01-31" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeTrue();
    }
}
