using Anela.Heblo.Domain.Features.Ecomail;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Ecomail;

public class EcomailModelMappingTests
{
    private static ApplicationDbContext CreateContext()
    {
        // Npgsql provider so relational metadata resolves; no connection is opened
        // because only the model is inspected.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=schema_inspection")
            .Options;
        return new ApplicationDbContext(options);
    }

    [Theory]
    [InlineData(typeof(EcomailCampaign), "EcomailCampaigns")]
    [InlineData(typeof(EcomailPipeline), "EcomailPipelines")]
    [InlineData(typeof(EcomailAutomationSnapshot), "EcomailAutomationSnapshots")]
    [InlineData(typeof(EcomailAutomationMonth), "EcomailAutomationMonths")]
    public void maps_each_entity_to_its_pascal_case_table_in_public_schema(Type entity, string table)
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(entity);

        entityType.Should().NotBeNull($"{entity.Name} must be registered on ApplicationDbContext");
        entityType!.GetTableName().Should().Be(table);
        entityType.GetSchema().Should().Be("public");
    }

    [Fact]
    public void snapshot_is_unique_per_pipeline_and_capture_date()
    {
        using var context = CreateContext();

        var indexes = context.Model.FindEntityType(typeof(EcomailAutomationSnapshot))!.GetIndexes();

        indexes.Should().ContainSingle(i =>
                i.IsUnique &&
                i.Properties.Select(p => p.Name).SequenceEqual(new[] { "PipelineId", "CapturedOn" }),
            "one snapshot per automation per day is what makes the job safe to re-run");
    }

    [Fact]
    public void automation_month_is_unique_per_pipeline_and_month()
    {
        using var context = CreateContext();

        var indexes = context.Model.FindEntityType(typeof(EcomailAutomationMonth))!.GetIndexes();

        indexes.Should().ContainSingle(i =>
            i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "PipelineId", "Year", "Month" }));
    }

    [Fact]
    public void every_datetime_column_is_timestamp_without_time_zone()
    {
        using var context = CreateContext();
        var entities = new[]
        {
            typeof(EcomailCampaign), typeof(EcomailPipeline),
            typeof(EcomailAutomationSnapshot), typeof(EcomailAutomationMonth)
        };

        foreach (var entity in entities)
        {
            var dateTimeProps = context.Model.FindEntityType(entity)!.GetProperties()
                .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?));

            foreach (var prop in dateTimeProps)
            {
                prop.GetColumnType().Should().Be("timestamp",
                    $"{entity.Name}.{prop.Name} must use AsUtcTimestamp() or Postgres rejects every write");
            }
        }
    }

    [Theory]
    [InlineData(3, "email", true)]
    [InlineData(3, "ab", true)]
    [InlineData(3, "variation", false)]   // an A/B arm, already inside its parent's totals
    [InlineData(3, "sms", false)]         // not email at all
    [InlineData(0, "email", false)]       // draft
    public void reportable_selects_only_sent_email_and_ab_campaigns(int status, string type, bool expected)
    {
        var campaign = new EcomailCampaign { Status = status, CampaignType = type };

        campaign.IsReportable.Should().Be(expected);
    }
}
