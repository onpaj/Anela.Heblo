using System.Reflection;
using Anela.Heblo.Application.Shared;
using Xunit;

namespace Anela.Heblo.Tests;

/// <summary>
/// Tests to ensure all API responses follow the standardized error handling structure
/// </summary>
public class ErrorHandlingTests
{
    public static IEnumerable<object[]> GetResponseClasses()
    {
        var applicationAssembly = Assembly.Load("Anela.Heblo.Application");
        var responseClasses = applicationAssembly.GetTypes()
            .Where(type => type.Name.EndsWith("Response", StringComparison.Ordinal) &&
                          type.IsClass &&
                          !type.IsAbstract &&
                          type != typeof(BaseResponse) &&
                          type != typeof(ListResponse<>))
            .ToList();

        foreach (var responseClass in responseClasses)
        {
            yield return new object[] { responseClass };
        }
    }

    [Theory]
    [MemberData(nameof(GetResponseClasses))]
    public void ResponseClass_ShouldInheritFromBaseResponse(Type responseClass)
    {
        // Act
        var inheritsFromBaseResponse = responseClass.IsSubclassOf(typeof(BaseResponse));

        // Assert
        Assert.True(inheritsFromBaseResponse,
            $"Response class '{responseClass.FullName}' must inherit from BaseResponse");
    }

    [Fact]
    public void ShouldFindSomeResponseClasses()
    {
        // Arrange & Act
        var responseClasses = GetResponseClasses().ToList();

        // Assert
        Assert.True(responseClasses.Count > 0, "Should find at least some response classes to test");
    }

    [Fact]
    public void ErrorCodes_ShouldHaveUniqueValues()
    {
        // Arrange
        var errorCodeValues = Enum.GetValues<ErrorCodes>().Cast<int>().ToList();

        // Act & Assert
        var duplicates = errorCodeValues
            .GroupBy(x => x)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void ErrorCodes_ShouldFollowModulePrefixSystem()
    {
        // Arrange & Act
        var errorCodes = Enum.GetValues<ErrorCodes>().Cast<int>().ToList();

        // Assert - Check that error codes follow the new prefix-postfix system
        var generalErrors = errorCodes.Where(code => code >= 1 && code <= 99).ToList(); // 00XX range
        var auditErrors = errorCodes.Where(code => code >= 1000 && code < 1100).ToList(); // 10XX range
        var purchaseErrors = errorCodes.Where(code => code >= 1100 && code < 1200).ToList(); // 11XX range
        var manufactureErrors = errorCodes.Where(code => code >= 1200 && code < 1300).ToList(); // 12XX range
        var catalogErrors = errorCodes.Where(code => code >= 1300 && code < 1400).ToList(); // 13XX range
        var transportErrors = errorCodes.Where(code => code >= 1400 && code < 1500).ToList(); // 14XX range
        var configErrors = errorCodes.Where(code => code >= 1500 && code < 1600).ToList(); // 15XX range
        var journalErrors = errorCodes.Where(code => code >= 1600 && code < 1700).ToList(); // 16XX range
        var analyticsErrors = errorCodes.Where(code => code >= 1700 && code < 1800).ToList(); // 17XX range
        var fileStorageErrors = errorCodes.Where(code => code >= 1800 && code < 1900).ToList(); // 18XX range
        var backgroundJobsErrors = errorCodes.Where(code => code >= 1900 && code < 2000).ToList(); // 19XX range
        var externalServiceErrors = errorCodes.Where(code => code >= 9000 && code < 9100).ToList(); // 90XX range

        // Ensure we have some errors in the expected categories
        Assert.True(generalErrors.Count > 0, "Should have general errors in 00XX range");
        Assert.True(purchaseErrors.Count > 0, "Should have purchase errors in 11XX range");
        Assert.True(catalogErrors.Count > 0, "Should have catalog errors in 13XX range");
        Assert.True(transportErrors.Count > 0, "Should have transport errors in 14XX range");
        Assert.True(configErrors.Count > 0, "Should have config errors in 15XX range");
        Assert.True(journalErrors.Count > 0, "Should have journal errors in 16XX range");
        Assert.True(analyticsErrors.Count > 0, "Should have analytics errors in 17XX range");
        Assert.True(fileStorageErrors.Count > 0, "Should have file storage errors in 18XX range");
        Assert.True(backgroundJobsErrors.Count > 0, "Should have background jobs errors in 19XX range");
        Assert.True(externalServiceErrors.Count > 0, "Should have external service errors in 90XX range");

        // Ensure all error codes fall into defined module ranges
        var categorizedCount = generalErrors.Count + auditErrors.Count + purchaseErrors.Count +
                              manufactureErrors.Count + catalogErrors.Count + transportErrors.Count +
                              configErrors.Count + journalErrors.Count + analyticsErrors.Count +
                              fileStorageErrors.Count + backgroundJobsErrors.Count + externalServiceErrors.Count;

        Assert.Equal(errorCodes.Count, categorizedCount);
    }

    [Fact]
    public void BaseResponse_ShouldHaveCorrectDefaultValues()
    {
        // Act
        var response = new TestResponse();

        // Assert
        Assert.True(response.Success);
        Assert.Null(response.ErrorCode);
        Assert.Null(response.Params);
    }

    [Fact]
    public void BaseResponse_ShouldAllowErrorCreation()
    {
        // Arrange
        var errorCode = ErrorCodes.ValidationError;
        var parameters = new Dictionary<string, string> { { "field", "testField" } };

        // Act
        var response = new TestResponse(errorCode, parameters);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(errorCode, response.ErrorCode);
        Assert.Equal(parameters, response.Params);
    }

    // Test response class for testing BaseResponse functionality
    private class TestResponse : BaseResponse
    {
        public TestResponse() : base() { }
        public TestResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
            : base(errorCode, parameters) { }
    }
}