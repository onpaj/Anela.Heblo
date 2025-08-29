using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Persistence.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests;

public class StagingEnvironmentTests
{
    [Fact]
    public void DatabaseSeeder_ShouldValidateTestDatabaseSuffix()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=Heblo_PROD;User Id=test;Password=test;"
            })
            .Build();

        var seeder = new DatabaseSeeder(null!, configuration, null!);

        // Act & Assert
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await seeder.TruncateAndSeedAsync());

        exception.Result.Message.Should().Contain("SAFETY CHECK FAILED");
        exception.Result.Message.Should().Contain("does not have TEST/TST suffix");
    }

    [Fact]
    public void StagingConfiguration_ShouldEnableAutoSeeding()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Staging.json", optional: true)
            .Build();

        // Act
        var enableAutoSeed = configuration.GetValue<bool>("DatabaseSeeding:EnableAutoSeed");
        var truncateOnStartup = configuration.GetValue<bool>("DatabaseSeeding:TruncateOnStartup");

        // Assert
        enableAutoSeed.Should().BeTrue();
        truncateOnStartup.Should().BeTrue();
    }

}