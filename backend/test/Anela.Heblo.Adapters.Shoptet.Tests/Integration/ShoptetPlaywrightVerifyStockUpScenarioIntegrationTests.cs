using Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration;

[Collection("ShoptetIntegration")]
[Trait("Category", "Integration")]
[Trait("Category", "Playwright")]
public class ShoptetPlaywrightVerifyStockUpScenarioIntegrationTests
{
    private readonly ShoptetIntegrationTestFixture _fixture;
    private readonly IConfiguration _configuration;
    private readonly VerifyStockUpScenario _verifyStockUpScenario;
    private readonly ITestOutputHelper _output;

    public ShoptetPlaywrightVerifyStockUpScenarioIntegrationTests(
        ShoptetIntegrationTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _configuration = fixture.Configuration;
        _verifyStockUpScenario = fixture.ServiceProvider.GetRequiredService<VerifyStockUpScenario>();
        _output = output;
    }

    [Fact]
    public async Task RunAsync_WithNonExistingDocumentNumber_ReturnsFalse()
    {
        // Arrange - Skip if configuration is not available
        HasValidConfiguration().Should().BeTrue("Shoptet credentials not configured or contain test placeholders");

        var nonExistingDocNumber = $"TEST-NONEXISTING-{DateTime.Now:yyyyMMddHHmmss}";
        _output.WriteLine($"Testing VerifyStockUpScenario with non-existing document number: {nonExistingDocNumber}");

        // Act
        var result = await _verifyStockUpScenario.RunAsync(nonExistingDocNumber);

        // Assert
        result.Should().BeFalse("Non-existing document should not be found in stock history");
        _output.WriteLine("✅ VerifyStockUpScenario correctly returned false for non-existing document");
    }

    [Fact]
    public async Task RunAsync_WithExistingDocumentNumber_ReturnsTrue()
    {
        // Arrange - Skip if configuration is not available
        HasValidConfiguration().Should().BeTrue("Shoptet credentials not configured or contain test placeholders");

        // Real document number that exists in Shoptet stock history
        var existingDocNumber = "BOX-003152-OCH004050";
        _output.WriteLine($"Testing VerifyStockUpScenario with existing document number: {existingDocNumber}");

        // Act
        var result = await _verifyStockUpScenario.RunAsync(existingDocNumber);

        // Assert
        result.Should().BeTrue("Existing document should be found in stock history");
        _output.WriteLine("✅ VerifyStockUpScenario correctly returned true for existing document");
        _output.WriteLine($"   Document {existingDocNumber} was successfully verified in stock history");
    }

    [Fact]
    public async Task RunAsync_ExpandsFilterPanel_BeforeFillingInputs()
    {
        // Arrange - Skip if configuration is not available
        HasValidConfiguration().Should().BeTrue("Shoptet credentials not configured or contain test placeholders");

        var testDocNumber = $"FILTER-TEST-{DateTime.Now:yyyyMMddHHmmss}";
        _output.WriteLine($"Testing filter expansion with document number: {testDocNumber}");
        _output.WriteLine("This test verifies that the filter panel is properly expanded before filling inputs");

        // Act - This should not throw TimeoutException anymore
        var act = async () => await _verifyStockUpScenario.RunAsync(testDocNumber);

        // Assert
        await act.Should().NotThrowAsync<TimeoutException>("Filter panel should expand successfully");
        _output.WriteLine("✅ Filter panel expanded correctly - no TimeoutException thrown");
    }

    private bool HasValidConfiguration()
    {
        var url = _configuration["ShoptetPlaywright:ShopEntryUrl"];
        var username = _configuration["ShoptetPlaywright:Login"];
        var password = _configuration["ShoptetPlaywright:Password"];

        return !string.IsNullOrEmpty(url) &&
               !string.IsNullOrEmpty(username) &&
               !string.IsNullOrEmpty(password) &&
               !url.Contains("your-shoptet") &&
               !username.Contains("test-username") &&
               !password.Contains("test-password");
    }
}
