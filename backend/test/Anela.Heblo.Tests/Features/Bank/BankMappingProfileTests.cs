using Anela.Heblo.Application.Features.Bank;
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Domain.Features.Bank;
using AutoMapper;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class BankMappingProfileTests
{
    private static IMapper CreateMapper()
    {
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<BankMappingProfile>());
        return configuration.CreateMapper();
    }

    [Fact]
    public void Profile_Configuration_IsValid()
    {
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<BankMappingProfile>());

        configuration.AssertConfigurationIsValid();
    }

    [Fact]
    public void Map_BankStatementImport_To_Dto_When_ImportResult_Is_OK_Sets_ErrorType_To_Null()
    {
        var mapper = CreateMapper();
        var source = new BankStatementImport("transfer-1", new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc))
        {
            ImportResult = "OK",
        };

        var dto = mapper.Map<BankStatementImportDto>(source);

        dto.ImportResult.Should().Be("OK");
        dto.ErrorType.Should().BeNull();
    }

    [Fact]
    public void Map_BankStatementImport_To_Dto_When_ImportResult_Is_Not_OK_Sets_ErrorType_To_ImportResult()
    {
        var mapper = CreateMapper();
        var source = new BankStatementImport("transfer-2", new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc))
        {
            ImportResult = "Failed",
        };

        var dto = mapper.Map<BankStatementImportDto>(source);

        dto.ImportResult.Should().Be("Failed");
        dto.ErrorType.Should().Be("Failed");
    }
}
