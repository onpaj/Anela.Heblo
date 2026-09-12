using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class RecurringJobConfigurationTests
{
    [Fact]
    public void RecurringJobConfiguration_ShouldCreateWithValidProperties()
    {
        // Arrange & Act
        var config = new RecurringJobConfiguration(
            jobName: "purchase-price-recalculation",
            displayName: "Purchase Price Recalculation",
            description: "Daily purchase price recalculation job",
            cronExpression: "0 2 * * *",
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        );

        // Assert
        Assert.Equal("purchase-price-recalculation", config.JobName);
        Assert.Equal("purchase-price-recalculation", config.Id); // JobName is the primary key
        Assert.Equal("Purchase Price Recalculation", config.DisplayName);
        Assert.Equal("Daily purchase price recalculation job", config.Description);
        Assert.Equal("0 2 * * *", config.CronExpression);
        Assert.Equal("Europe/Prague", config.TimeZoneId);
        Assert.True(config.IsEnabled);
        Assert.Equal("system", config.LastModifiedBy);
    }

    [Fact]
    public void RecurringJobConfiguration_ShouldSetTimeZoneId_WhenGivenNonDefaultValue()
    {
        // Arrange & Act
        var config = new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "0 0 * * *",
            timeZoneId: "America/New_York",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        );

        // Assert
        Assert.Equal("America/New_York", config.TimeZoneId);
    }

    [Fact]
    public void RecurringJobConfiguration_ShouldAllowDisabling()
    {
        // Arrange
        var config = new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "0 0 * * *",
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        );

        // Act
        config.Disable("admin", DateTime.UtcNow);

        // Assert
        Assert.False(config.IsEnabled);
        Assert.Equal("admin", config.LastModifiedBy);
    }

    [Fact]
    public void Constructor_ShouldThrowValidationException_WhenJobNameIsEmpty()
    {
        // Arrange & Act & Assert
        Assert.Throws<ValidationException>(() => new RecurringJobConfiguration(
            jobName: "",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "0 0 * * *",
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        ));
    }

    [Fact]
    public void Constructor_ShouldThrowValidationException_WhenDisplayNameIsEmpty()
    {
        // Arrange & Act & Assert
        Assert.Throws<ValidationException>(() => new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "",
            description: "Test description",
            cronExpression: "0 0 * * *",
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        ));
    }

    [Fact]
    public void Enable_ShouldSetIsEnabledToTrue()
    {
        // Arrange
        var config = new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "0 0 * * *",
            timeZoneId: "Europe/Prague",
            isEnabled: false,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        );

        // Act
        config.Enable("admin", DateTime.UtcNow);

        // Assert
        Assert.True(config.IsEnabled);
        Assert.Equal("admin", config.LastModifiedBy);
    }

    [Fact]
    public void Disable_ShouldSetIsEnabledToFalse()
    {
        // Arrange
        var config = new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "0 0 * * *",
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        );

        // Act
        config.Disable("admin", DateTime.UtcNow);

        // Assert
        Assert.False(config.IsEnabled);
        Assert.Equal("admin", config.LastModifiedBy);
    }

    [Fact]
    public void UpdateConfiguration_ShouldUpdateProperties()
    {
        // Arrange
        var config = new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "0 0 * * *",
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        );

        // Act
        config.UpdateConfiguration(
            displayName: "Updated Job",
            description: "Updated description",
            cronExpression: "0 2 * * *",
            timeZoneId: "America/New_York",
            modifiedBy: "admin",
            modifiedAt: DateTime.UtcNow
        );

        // Assert
        Assert.Equal("Updated Job", config.DisplayName);
        Assert.Equal("Updated description", config.Description);
        Assert.Equal("0 2 * * *", config.CronExpression);
        Assert.Equal("America/New_York", config.TimeZoneId);
        Assert.Equal("admin", config.LastModifiedBy);
    }

    [Fact]
    public void Constructor_ShouldThrowValidationException_WhenTimeZoneIdIsEmpty()
    {
        // Arrange & Act & Assert
        Assert.Throws<ValidationException>(() => new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "0 0 * * *",
            timeZoneId: "   ",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        ));
    }

    [Fact]
    public void UpdateConfiguration_ShouldThrowValidationException_WhenTimeZoneIdIsEmpty()
    {
        // Arrange
        var config = new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "0 0 * * *",
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        );

        // Act & Assert
        Assert.Throws<ValidationException>(() => config.UpdateConfiguration(
            displayName: "Updated Job",
            description: "Updated description",
            cronExpression: "0 2 * * *",
            timeZoneId: "",
            modifiedBy: "admin",
            modifiedAt: DateTime.UtcNow
        ));
    }

    [Fact]
    public void Enable_ShouldThrowValidationException_WhenModifiedByIsEmpty()
    {
        // Arrange
        var config = new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "0 0 * * *",
            timeZoneId: "Europe/Prague",
            isEnabled: false,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        );

        // Act & Assert
        Assert.Throws<ValidationException>(() => config.Enable("", DateTime.UtcNow));
    }

    [Fact]
    public void Disable_ShouldThrowValidationException_WhenModifiedByIsEmpty()
    {
        // Arrange
        var config = new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "0 0 * * *",
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        );

        // Act & Assert
        Assert.Throws<ValidationException>(() => config.Disable("", DateTime.UtcNow));
    }

    [Fact]
    public void Constructor_ShouldThrowValidationException_WhenCronExpressionIsMalformed()
    {
        // Arrange & Act & Assert
        Assert.Throws<ValidationException>(() => new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "not-a-cron",
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        ));
    }

    [Fact]
    public void UpdateConfiguration_ShouldThrowValidationException_WhenCronExpressionIsMalformed()
    {
        // Arrange
        var config = new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "0 0 * * *",
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        );

        // Act & Assert
        Assert.Throws<ValidationException>(() => config.UpdateConfiguration(
            displayName: "Updated Job",
            description: "Updated description",
            cronExpression: "not-a-cron",
            timeZoneId: "Europe/Prague",
            modifiedBy: "admin",
            modifiedAt: DateTime.UtcNow
        ));

        // CronExpression must remain unchanged after the throw
        Assert.Equal("0 0 * * *", config.CronExpression);
    }

    [Fact]
    public void UpdateCronExpression_ShouldThrowValidationException_WhenCronExpressionIsMalformed()
    {
        // Arrange
        var config = new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: "0 0 * * *",
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        );

        // Act & Assert
        Assert.Throws<ValidationException>(() => config.UpdateCronExpression("not-a-cron", "admin", DateTime.UtcNow));

        // CronExpression must remain unchanged after the throw
        Assert.Equal("0 0 * * *", config.CronExpression);
    }

    [Theory]
    [InlineData("0 2 * * *")]           // 5-field standard, existing test fixture value
    [InlineData("*/15 * * * *")]        // 5-field standard, from RagFeatureOptions default
    [InlineData("0 6,18 * * *")]        // 5-field standard, from MetaAdsInvoiceImportJob
    [InlineData("15 6,18 * * *")]       // 5-field standard, from GoogleAdsInvoiceImportJob
    [InlineData("0 * * * *")]           // 5-field standard, from CompleteDeliveredOrdersJob
    [InlineData("0 0 0 * * *")]         // 6-field Quartz-style (leading seconds)
    public void Constructor_ShouldAccept_KnownValidCronExpressions(string cronExpression)
    {
        // Arrange & Act
        var config = new RecurringJobConfiguration(
            jobName: "test-job",
            displayName: "Test Job",
            description: "Test description",
            cronExpression: cronExpression,
            timeZoneId: "Europe/Prague",
            isEnabled: true,
            lastModifiedBy: "system",
            lastModifiedAt: DateTime.UtcNow
        );

        // Assert
        Assert.Equal(cronExpression, config.CronExpression);
    }
}
