using System.Reflection;
using System.Text.RegularExpressions;
using Anela.Heblo.Application.Shared;
using Xunit;

namespace Anela.Heblo.Tests;

/// <summary>
/// Tests to ensure all error codes have corresponding translations in frontend i18n files
/// </summary>
public class LocalizationCoverageTests
{
    private const string FrontendI18nFilePath = "../../../../../frontend/src/i18n.ts";

    [Fact]
    public void FrontendI18n_ShouldHaveTranslationsForAllErrorCodes()
    {
        // Arrange
        var errorCodeValues = Enum.GetValues<ErrorCodes>().Cast<int>().ToList();

        // Try to find the frontend i18n file
        // From test assembly location, find the project root and then frontend folder
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var testDirectory = Path.GetDirectoryName(assemblyLocation)!;

        // Navigate up from test assembly location to find project root
        var projectRoot = testDirectory;
        while (projectRoot != null && !File.Exists(Path.Combine(projectRoot, "frontend", "src", "i18n.ts")))
        {
            var parent = Directory.GetParent(projectRoot);
            if (parent == null) break;
            projectRoot = parent.FullName;
        }

        Assert.True(projectRoot != null, "Could not find project root directory");

        var i18nFilePath = Path.Combine(projectRoot, "frontend", "src", "i18n.ts");
        Assert.True(File.Exists(i18nFilePath), $"Frontend i18n file not found at: {i18nFilePath}");

        var i18nContent = File.ReadAllText(i18nFilePath);

        // Simply search in the entire file content for error code translations
        // This is more reliable than trying to parse the complex nested structure
        var missingTranslations = new List<int>();

        // Act & Assert
        foreach (var errorCode in errorCodeValues)
        {
            // Check for string key format ('ValidationError', 'PurchaseOrderNotFound', etc.)
            var errorCodeName = ((ErrorCodes)errorCode).ToString();
            var hasStringTranslation = Regex.IsMatch(i18nContent, $@"'{errorCodeName}':\s*'[^']*'", RegexOptions.Multiline);

            // Check for direct numeric format ('1', '1101', etc.) in entire file
            var hasDirectTranslation = Regex.IsMatch(i18nContent, $@"'{errorCode}':\s*'[^']*'", RegexOptions.Multiline);

            // Check for padded format ('0001', etc.) - only for codes < 100
            var hasPaddedTranslation = errorCode < 100 &&
                                     Regex.IsMatch(i18nContent, $@"'{errorCode:D4}':\s*'[^']*'", RegexOptions.Multiline);

            // Check legacy mappings for backward compatibility
            var hasLegacyTranslation = CheckLegacyTranslation(i18nContent, errorCode);

            if (!hasStringTranslation && !hasDirectTranslation && !hasPaddedTranslation && !hasLegacyTranslation)
            {
                missingTranslations.Add(errorCode);
            }
        }

        // Report missing translations
        if (missingTranslations.Count > 0)
        {
            var missingList = string.Join(", ", missingTranslations);
            Assert.Fail($"Missing Czech translations for error codes: {missingList}. " +
                       $"Please add translations in frontend/src/i18n.ts errors section.");
        }

        // Ensure we found some error codes (sanity check)
        Assert.True(errorCodeValues.Count > 0, "Should find at least some error codes to test");
    }

    [Fact]
    public void ErrorCodes_ShouldFollowNewModulePrefixFormat()
    {
        // Arrange & Act
        var errorCodeValues = Enum.GetValues<ErrorCodes>().Cast<int>().ToList();

        // Assert - Check that we have error codes in expected module ranges
        var generalErrors = errorCodeValues.Where(code => code >= 1 && code <= 99).ToList();
        var purchaseErrors = errorCodeValues.Where(code => code >= 1100 && code < 1200).ToList();
        var catalogErrors = errorCodeValues.Where(code => code >= 1300 && code < 1400).ToList();
        var transportErrors = errorCodeValues.Where(code => code >= 1400 && code < 1500).ToList();
        var configErrors = errorCodeValues.Where(code => code >= 1500 && code < 1600).ToList();
        var externalErrors = errorCodeValues.Where(code => code >= 9000 && code < 9100).ToList();

        // Verify we have the expected distribution
        Assert.True(generalErrors.Count > 0, "Should have general errors (00XX range: 1-99)");
        Assert.True(purchaseErrors.Count > 0, "Should have purchase errors (11XX range: 1100-1199)");
        Assert.True(catalogErrors.Count > 0, "Should have catalog errors (13XX range: 1300-1399)");
        Assert.True(transportErrors.Count > 0, "Should have transport errors (14XX range: 1400-1499)");
        Assert.True(configErrors.Count > 0, "Should have config errors (15XX range: 1500-1599)");
        Assert.True(externalErrors.Count > 0, "Should have external service errors (90XX range: 9000-9099)");
    }

    private static bool CheckLegacyTranslation(string fileContent, int errorCode)
    {
        // Check for legacy error code mappings that might exist for backward compatibility
        var legacyMappings = new Dictionary<int, string[]>
        {
            { 1, new[] { "1000" } }, // ValidationError might also be mapped as '1000'
            { 1101, new[] { "2001" } }, // PurchaseOrderNotFound might also be mapped as '2001'
            { 8, new[] { "3001" } }, // InvalidOperation might also be mapped as '3001'
            { 10, new[] { "5000" } }, // InternalServerError might also be mapped as '5000'
            { 13, new[] { "6000" } } // Unauthorized might also be mapped as '6000'
        };

        if (legacyMappings.TryGetValue(errorCode, out var legacyCodes))
        {
            return legacyCodes.Any(legacyCode =>
                Regex.IsMatch(fileContent, $@"'{legacyCode}':\s*'[^']*'"));
        }

        return false;
    }
}