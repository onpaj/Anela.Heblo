using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Bank;
using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

[Trait("Category", "Integration")]
public class BankStatementImportRepositoryIntegrationTests : IAsyncLifetime
{
    static BankStatementImportRepositoryIntegrationTests()
    {
        // Required on macOS with Podman: the Ryuk ResourceReaper container
        // cannot bind to the Docker socket and throws a NullReferenceException.
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    private ApplicationDbContext _context = null!;
    private BankStatementImportRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        _context = new ApplicationDbContext(options);

        // Create only the BankStatements table manually
        // Do NOT use EnsureCreatedAsync because it would try to install the "vector" extension
        // which is not available in the plain postgres:16 image
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE SCHEMA IF NOT EXISTS public;
                CREATE TABLE IF NOT EXISTS public."BankStatements" (
                    "Id"            serial                       PRIMARY KEY,
                    "TransferId"    character varying(100)       NOT NULL,
                    "StatementDate" timestamp without time zone  NOT NULL,
                    "ImportDate"    timestamp without time zone  NOT NULL,
                    "Account"       text                         NOT NULL,
                    "Currency"      integer                      NOT NULL,
                    "ItemCount"     integer                      NOT NULL,
                    "ImportResult"  text                         NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_BankStatements_TransferId"
                    ON public."BankStatements" ("TransferId");
                CREATE INDEX IF NOT EXISTS "IX_BankStatements_Account"
                    ON public."BankStatements" ("Account");
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        _repository = new BankStatementImportRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task<BankStatementImport> SeedAsync(
        string transferId,
        string account,
        string importResult = "OK",
        CurrencyCode currency = CurrencyCode.CZK,
        int itemCount = 0)
    {
        var statement = new BankStatementImport(transferId, DateTime.UtcNow);
        statement.Account = account;
        statement.Currency = currency;
        statement.ItemCount = itemCount;
        // Use reflection to set ImportResult since it's private with property validation
        var prop = typeof(BankStatementImport).GetProperty("ImportResult");
        prop?.SetValue(statement, importResult);

        return await _repository.AddAsync(statement);
    }

    [Fact]
    public async Task GetFilteredAsync_TransferIdSubstring_MatchesCaseInsensitive()
    {
        // Arrange
        await SeedAsync("ABC-123", "ShoptetPay-CZK");
        await SeedAsync("XYZ-999", "ShoptetPay-CZK");

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(
            new BankStatementListFilter(TransferId: "abc"));

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(i => i.TransferId == "ABC-123");
    }

    [Fact]
    public async Task GetFilteredAsync_AccountSubstring_MatchesCaseInsensitive()
    {
        // Arrange
        await SeedAsync("T1", "ShoptetPay-CZK");
        await SeedAsync("T2", "Comgate-EUR");

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(
            new BankStatementListFilter(Account: "shoptet"));

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(i => i.TransferId == "T1");
    }

    [Fact]
    public async Task GetFilteredAsync_AccountSubstring_TrimsWhitespace()
    {
        // Arrange
        await SeedAsync("T1", "ShoptetPay-CZK");

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(
            new BankStatementListFilter(Account: "  shoptet  "));

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(i => i.TransferId == "T1");
    }

    [Fact]
    public async Task GetFilteredAsync_AccountSubstring_EscapesPercentWildcard()
    {
        // Arrange
        await SeedAsync("T1", "Acct-50%-rate");
        await SeedAsync("T2", "Acct-50-rate");

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(
            new BankStatementListFilter(Account: "50%"));

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(i => i.TransferId == "T1");
    }

    [Fact]
    public async Task GetFilteredAsync_AccountSubstring_EscapesUnderscoreWildcard()
    {
        // Arrange
        await SeedAsync("T1", "Acct_X");
        await SeedAsync("T2", "AcctYX");

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(
            new BankStatementListFilter(Account: "Acct_X"));

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(i => i.TransferId == "T1");
    }

    [Fact]
    public async Task GetFilteredAsync_TransferIdAndAccount_CombineWithAndSemantics()
    {
        // Arrange
        await SeedAsync("ABC-100", "ShoptetPay-CZK");
        await SeedAsync("ABC-200", "Comgate-EUR");
        await SeedAsync("XYZ-300", "ShoptetPay-CZK");

        // Act
        var (items, totalCount) = await _repository.GetFilteredAsync(
            new BankStatementListFilter(TransferId: "abc", Account: "shoptet"));

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(i => i.TransferId == "ABC-100");
    }
}
