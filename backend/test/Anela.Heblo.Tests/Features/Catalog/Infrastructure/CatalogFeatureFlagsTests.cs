using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

// Test helper class for IHostEnvironment
public class TestHostEnvironment : IHostEnvironment
{
    public TestHostEnvironment(string environmentName)
    {
        EnvironmentName = environmentName;
        ApplicationName = "TestApp";
        ContentRootPath = "/";
        ContentRootFileProvider = new NullFileProvider();
    }

    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; }
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; }
}

public class CatalogFeatureFlagsTests
{
    [Fact]
    public void CatalogFeatureFlags_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var flags = new CatalogFeatureFlags();

        // Assert
        flags.IsTransportBoxTrackingEnabled.Should().BeFalse();
        flags.IsStockTakingEnabled.Should().BeFalse();
        flags.IsBackgroundRefreshEnabled.Should().BeTrue();
    }

    [Fact]
    public void CatalogFeatureFlags_CanBeConfiguredViaProperties()
    {
        // Arrange
        var flags = new CatalogFeatureFlags();

        // Act
        flags.IsTransportBoxTrackingEnabled = true;
        flags.IsStockTakingEnabled = true;
        flags.IsBackgroundRefreshEnabled = false;

        // Assert
        flags.IsTransportBoxTrackingEnabled.Should().BeTrue();
        flags.IsStockTakingEnabled.Should().BeTrue();
        flags.IsBackgroundRefreshEnabled.Should().BeFalse();
    }

    [Fact]
    public void CatalogModule_ConfiguresFeatureFlags_WithDefaultValues()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        // Act
        services.AddCatalogModule();
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<CatalogFeatureFlags>>();

        // Assert
        options.Value.IsTransportBoxTrackingEnabled.Should().BeFalse();
        options.Value.IsStockTakingEnabled.Should().BeFalse();
        options.Value.IsBackgroundRefreshEnabled.Should().BeTrue(); // Development environment
    }

    [Fact]
    public void CatalogModule_ConfiguresFeatureFlags_AutomationEnvironment_DisablesBackgroundRefresh()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        // Act
        services.AddCatalogModule(new TestHostEnvironment("Automation"));
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<CatalogFeatureFlags>>();

        // Assert
        options.Value.IsTransportBoxTrackingEnabled.Should().BeFalse();
        options.Value.IsStockTakingEnabled.Should().BeFalse();
        options.Value.IsBackgroundRefreshEnabled.Should().BeFalse(); // Automation environment
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", true)]
    [InlineData("Test", true)]
    [InlineData("Staging", true)]
    [InlineData("Automation", false)]
    [InlineData("automation", true)] // Case sensitive - different from "Automation"
    [InlineData("AUTOMATION", true)] // Case sensitive - different from "Automation"
    public void CatalogModule_ConfiguresBackgroundRefresh_BasedOnEnvironment(string environmentName, bool expectedBackgroundRefreshEnabled)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        // Act
        services.AddCatalogModule(new TestHostEnvironment(environmentName));
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<CatalogFeatureFlags>>();

        // Assert
        options.Value.IsBackgroundRefreshEnabled.Should().Be(expectedBackgroundRefreshEnabled);
    }

    [Fact]
    public void CatalogModule_CanOverrideFeatureFlags_ViaConfiguration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CatalogFeatureFlags:IsTransportBoxTrackingEnabled"] = "true",
                ["CatalogFeatureFlags:IsStockTakingEnabled"] = "true",
                ["CatalogFeatureFlags:IsBackgroundRefreshEnabled"] = "false"
            }!)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        // Configure options from configuration
        services.Configure<CatalogFeatureFlags>(configuration.GetSection("CatalogFeatureFlags"));

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<CatalogFeatureFlags>>();

        // Assert
        options.Value.IsTransportBoxTrackingEnabled.Should().BeTrue();
        options.Value.IsStockTakingEnabled.Should().BeTrue();
        options.Value.IsBackgroundRefreshEnabled.Should().BeFalse();
    }

    [Fact]
    public void CatalogFeatureFlags_PropertyNames_FollowNamingConvention()
    {
        // Arrange
        var flags = new CatalogFeatureFlags();
        var type = typeof(CatalogFeatureFlags);

        // Act
        var properties = type.GetProperties();

        // Assert
        properties.Should().AllSatisfy(prop =>
        {
            prop.Name.Should().StartWith("Is");
            prop.Name.Should().EndWith("Enabled");
            prop.PropertyType.Should().Be(typeof(bool));
        });
    }

    [Fact]
    public void CatalogFeatureFlags_HasExpectedProperties()
    {
        // Arrange
        var type = typeof(CatalogFeatureFlags);

        // Act
        var propertyNames = type.GetProperties().Select(p => p.Name).ToList();

        // Assert
        propertyNames.Should().Contain("IsTransportBoxTrackingEnabled");
        propertyNames.Should().Contain("IsStockTakingEnabled");
        propertyNames.Should().Contain("IsBackgroundRefreshEnabled");
        propertyNames.Should().HaveCount(3); // Ensure no unexpected properties
    }

    [Fact]
    public void CatalogFeatureFlags_AllProperties_AreReadWrite()
    {
        // Arrange
        var type = typeof(CatalogFeatureFlags);

        // Act & Assert
        type.GetProperties().Should().AllSatisfy(prop =>
        {
            prop.CanRead.Should().BeTrue($"Property {prop.Name} should be readable");
            prop.CanWrite.Should().BeTrue($"Property {prop.Name} should be writable");
        });
    }

    [Fact]
    public void CatalogFeatureFlags_CanBeSerializedAndDeserialized()
    {
        // Arrange
        var originalFlags = new CatalogFeatureFlags
        {
            IsTransportBoxTrackingEnabled = true,
            IsStockTakingEnabled = true,
            IsBackgroundRefreshEnabled = false
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(originalFlags);
        var deserializedFlags = System.Text.Json.JsonSerializer.Deserialize<CatalogFeatureFlags>(json);

        // Assert
        deserializedFlags.Should().NotBeNull();
        deserializedFlags!.IsTransportBoxTrackingEnabled.Should().Be(originalFlags.IsTransportBoxTrackingEnabled);
        deserializedFlags.IsStockTakingEnabled.Should().Be(originalFlags.IsStockTakingEnabled);
        deserializedFlags.IsBackgroundRefreshEnabled.Should().Be(originalFlags.IsBackgroundRefreshEnabled);
    }
}