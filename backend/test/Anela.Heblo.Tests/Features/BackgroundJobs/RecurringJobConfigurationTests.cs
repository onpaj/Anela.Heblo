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
            isEnabled: true,
            lastModifiedBy: "system"
        );

        // Assert
        Assert.Equal("purchase-price-recalculation", config.JobName);
        Assert.Equal("purchase-price-recalculation", config.Id); // JobName is the primary key
        Assert.Equal("Purchase Price Recalculation", config.DisplayName);
        Assert.Equal("Daily purchase price recalculation job", config.Description);
        Assert.Equal("0 2 * * *", config.CronExpression);
        Assert.True(config.IsEnabled);
        Assert.Equal("system", config.LastModifiedBy);
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
            isEnabled: true,
            lastModifiedBy: "system"
        );

        // Act
        config.Disable("admin");

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
            isEnabled: true,
            lastModifiedBy: "system"
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
            isEnabled: true,
            lastModifiedBy: "system"
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
            isEnabled: false,
            lastModifiedBy: "system"
        );

        // Act
        config.Enable("admin");

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
            isEnabled: true,
            lastModifiedBy: "system"
        );

        // Act
        config.Disable("admin");

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
            isEnabled: true,
            lastModifiedBy: "system"
        );

        // Act
        config.UpdateConfiguration(
            displayName: "Updated Job",
            description: "Updated description",
            cronExpression: "0 2 * * *",
            modifiedBy: "admin"
        );

        // Assert
        Assert.Equal("Updated Job", config.DisplayName);
        Assert.Equal("Updated description", config.Description);
        Assert.Equal("0 2 * * *", config.CronExpression);
        Assert.Equal("admin", config.LastModifiedBy);
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
            isEnabled: false,
            lastModifiedBy: "system"
        );

        // Act & Assert
        Assert.Throws<ValidationException>(() => config.Enable(""));
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
            isEnabled: true,
            lastModifiedBy: "system"
        );

        // Act & Assert
        Assert.Throws<ValidationException>(() => config.Disable(""));
    }
}
