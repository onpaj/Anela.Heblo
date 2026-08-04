using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.InvoiceClassification;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.InvoiceClassification;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class ClassificationRuleRepositoryReorderIntegrationTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;
    private ApplicationDbContext _context = null!;
    private ClassificationRuleRepository _repository = null!;

    public ClassificationRuleRepositoryReorderIntegrationTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("classificationrules");

        // Create only the ClassificationRules table manually.
        // Do NOT use EnsureCreatedAsync/MigrateAsync — the project schema depends on the
        // "vector" extension which is not available in the plain postgres:16 image.
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE SCHEMA IF NOT EXISTS public;
            CREATE TABLE IF NOT EXISTS public."ClassificationRules" (
                "Id"                     uuid                          PRIMARY KEY,
                "Name"                   character varying(255)        NOT NULL,
                "RuleTypeIdentifier"     character varying(100)        NOT NULL,
                "Pattern"                character varying(1000)       NOT NULL,
                "AccountingTemplateCode" character varying(255)        NOT NULL,
                "Department"             character varying(255)        NULL,
                "Order"                  integer                       NOT NULL,
                "IsActive"               boolean                       NOT NULL DEFAULT true,
                "CreatedAt"              timestamp without time zone   NOT NULL,
                "UpdatedAt"              timestamp without time zone   NOT NULL,
                "CreatedBy"              character varying(255)        NOT NULL,
                "UpdatedBy"              character varying(255)        NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ClassificationRules_Order"
                ON public."ClassificationRules" ("Order");
            """;
        await cmd.ExecuteNonQueryAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new ClassificationRuleRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private static ClassificationRule CreateRule(string name, int order, bool isActive = true)
    {
        var rule = new ClassificationRule(
            name: name,
            ruleTypeIdentifier: "CompanyName",
            pattern: name,
            accountingTemplateCode: "TPL1",
            department: null,
            createdBy: "tester");
        rule.SetOrder(order);
        if (!isActive)
        {
            rule.Update(rule.Name, rule.RuleTypeIdentifier, rule.Pattern, rule.AccountingTemplateCode, rule.Department, isActive: false, updatedBy: "tester");
        }
        return rule;
    }

    private async Task SeedAsync(params ClassificationRule[] rules)
    {
        _context.ClassificationRules.AddRange(rules);
        await _context.SaveChangesAsync();
        // Detach so later reads via the repository/assertions hit the database, not the tracker.
        foreach (var rule in rules)
        {
            _context.Entry(rule).State = EntityState.Detached;
        }
    }

    private async Task<int> ReadOrderAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT "Order" FROM public."ClassificationRules" WHERE "Id" = @id""";
        cmd.Parameters.AddWithValue("id", id);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    [Fact]
    public async Task ReorderRulesAsync_FullDerangement_PersistsRequestedSequenceWithoutThrowing()
    {
        // Arrange
        var a = CreateRule("A", 1);
        var b = CreateRule("B", 2);
        var c = CreateRule("C", 3);
        await SeedAsync(a, b, c);

        // Act
        Func<Task> act = () => _repository.ReorderRulesAsync(new List<Guid> { c.Id, a.Id, b.Id });

        // Assert
        await act.Should().NotThrowAsync();
        (await ReadOrderAsync(c.Id)).Should().Be(1);
        (await ReadOrderAsync(a.Id)).Should().Be(2);
        (await ReadOrderAsync(b.Id)).Should().Be(3);
    }

    [Fact]
    public async Task ReorderRulesAsync_WhenAnInactiveRowHoldsAnIntermediateOrderValue_NeverCollidesWithIt()
    {
        // Arrange — 5 rows, the one at Order=2 is inactive and excluded from the reorder.
        var r1 = CreateRule("R1", 1);
        var inactive = CreateRule("R2", 2, isActive: false);
        var r3 = CreateRule("R3", 3);
        var r4 = CreateRule("R4", 4);
        var r5 = CreateRule("R5", 5);
        await SeedAsync(r1, inactive, r3, r4, r5);

        // Act — reorder the 4 active rules into a non-identity sequence.
        Func<Task> act = () => _repository.ReorderRulesAsync(new List<Guid> { r5.Id, r1.Id, r4.Id, r3.Id });

        // Assert
        await act.Should().NotThrowAsync();
        (await ReadOrderAsync(inactive.Id)).Should().Be(2, "the inactive row's Order must never be touched");
        (await ReadOrderAsync(r5.Id)).Should().Be(1);
        (await ReadOrderAsync(r1.Id)).Should().Be(3);
        (await ReadOrderAsync(r4.Id)).Should().Be(4);
        (await ReadOrderAsync(r3.Id)).Should().Be(5);
    }

    [Fact]
    public async Task ReorderRulesAsync_WithAnUnknownRuleId_SkipsItAndReordersTheRest()
    {
        // Arrange
        var a = CreateRule("A", 1);
        var b = CreateRule("B", 2);
        var c = CreateRule("C", 3);
        await SeedAsync(a, b, c);
        var unknownId = Guid.NewGuid();

        // Act
        Func<Task> act = () => _repository.ReorderRulesAsync(new List<Guid> { unknownId, c.Id, a.Id });

        // Assert
        await act.Should().NotThrowAsync();
        (await ReadOrderAsync(c.Id)).Should().Be(1);
        (await ReadOrderAsync(a.Id)).Should().Be(3);
        (await ReadOrderAsync(b.Id)).Should().Be(2, "rule not named in ruleIds must be left untouched");
    }
}
