using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Shared;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Anela.Heblo.Tests;

/// <summary>
/// Tests to ensure controllers return structured error responses instead of raw strings
/// </summary>
public class PurchaseOrdersControllerErrorTests
{
    [Fact]
    public void ControllerMethods_ShouldNotReturnRawStringErrors()
    {
        // Arrange
        var controllerType = typeof(PurchaseOrdersController);
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.IsPublic &&
                       !m.IsSpecialName &&
                       m.DeclaringType == controllerType &&
                       m.GetCustomAttributes<HttpGetAttribute>().Any() ||
                       m.GetCustomAttributes<HttpPostAttribute>().Any() ||
                       m.GetCustomAttributes<HttpPutAttribute>().Any() ||
                       m.GetCustomAttributes<HttpDeleteAttribute>().Any())
            .ToList();

        // Act & Assert
        foreach (var method in methods)
        {
            var returnType = method.ReturnType;

            // Check if it's Task<ActionResult<T>> or ActionResult<T>
            Type? responseType = null;
            if (returnType.IsGenericType)
            {
                var genericArgs = returnType.GetGenericArguments();
                if (genericArgs.Length > 0)
                {
                    var actionResultType = genericArgs[0];
                    if (actionResultType.IsGenericType && actionResultType.GetGenericTypeDefinition() == typeof(ActionResult<>))
                    {
                        responseType = actionResultType.GetGenericArguments()[0];
                    }
                }
            }

            if (responseType != null)
            {
                var inheritsFromBaseResponse = responseType.IsSubclassOf(typeof(BaseResponse)) ||
                    (responseType.IsGenericType &&
                     responseType.GetGenericTypeDefinition() == typeof(ListResponse<>));

                Assert.True(inheritsFromBaseResponse,
                    $"Controller method '{method.Name}' should return a type that inherits from BaseResponse. " +
                    $"Currently returns: {responseType.Name}");
            }
        }

        // Sanity check - ensure we found some methods to test
        Assert.True(methods.Count > 0, "Should find at least some controller methods to test");
    }

    [Fact]
    public void ErrorResponseHelper_ShouldCreateValidErrorResponse()
    {
        // Act
        var response = API.Infrastructure.ErrorResponseHelper
            .CreateErrorResponse<TestResponse>(ErrorCodes.ValidationError,
                new Dictionary<string, string> { { "field", "test" } });

        // Assert
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.ValidationError, response.ErrorCode);
        Assert.NotNull(response.Params);
        Assert.Equal("test", response.Params["field"]);
    }

    [Fact]
    public void ErrorResponseHelper_ShouldCreateValidationError()
    {
        // Act
        var response = API.Infrastructure.ErrorResponseHelper
            .CreateValidationError<TestResponse>("testField");

        // Assert
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.ValidationError, response.ErrorCode);
        Assert.NotNull(response.Params);
        Assert.Equal("testField", response.Params["field"]);
    }

    [Fact]
    public void ErrorResponseHelper_ShouldCreateNotFoundError()
    {
        // Act
        var response = API.Infrastructure.ErrorResponseHelper
            .CreateNotFoundError<TestResponse>(ErrorCodes.PurchaseOrderNotFound, "123");

        // Assert
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.PurchaseOrderNotFound, response.ErrorCode);
        Assert.NotNull(response.Params);
        Assert.Equal("123", response.Params["id"]);
    }

    // Test response class
    private class TestResponse : BaseResponse
    {
        public TestResponse() : base() { }
        public TestResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
            : base(errorCode, parameters) { }
    }
}