using Anela.Heblo.API.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure;

public class UrlMappingModelBinderTests
{
    private readonly UrlMappingModelBinder _binder;

    public UrlMappingModelBinderTests()
    {
        _binder = new UrlMappingModelBinder();
    }

    private ModelBindingContext CreateBindingContext<T>(Dictionary<string, string> queryParams)
    {
        var httpContext = new DefaultHttpContext();

        // Set up query parameters
        var queryCollection = new QueryCollection(queryParams.ToDictionary(
            kvp => kvp.Key,
            kvp => new StringValues(kvp.Value)
        ));
        httpContext.Request.Query = queryCollection;

        var modelMetadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(T));
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        var bindingContext = new DefaultModelBindingContext();

        // Initialize the binding context properly using methods that set internal state
        bindingContext.ActionContext = actionContext;
        bindingContext.ModelMetadata = modelMetadata;
        bindingContext.ModelName = "request";
        bindingContext.ModelState = new ModelStateDictionary();

        return bindingContext;
    }

    // Test model for various data types
    public class TestRequest
    {
        public string? ProductCode { get; set; }
        public int Skip { get; set; }
        public int Take { get; set; }
        public double Price { get; set; }
        public decimal Amount { get; set; }
        public float Weight { get; set; }
        public bool SortDescending { get; set; }
        public bool Active { get; set; }
        public string? State { get; set; }
        public int? Count { get; set; }
        public DateTime? CreatedDate { get; set; }
    }

    [Fact]
    public async Task ShouldMapStringParameter()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["productCode"] = "ABC123"
        };
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.Equal("ABC123", model.ProductCode);
    }

    [Fact]
    public async Task ShouldMapIntParameter()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["skip"] = "20",
            ["take"] = "50"
        };
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.Equal(20, model.Skip);
        Assert.Equal(50, model.Take);
    }

    [Fact]
    public async Task ShouldMapDoubleParameter()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["price"] = "99.99"
        };
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.Equal(99.99d, model.Price);
    }

    [Fact]
    public async Task ShouldMapDecimalParameter()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["amount"] = "123.45"
        };
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.Equal(123.45m, model.Amount);
    }

    [Fact]
    public async Task ShouldMapFloatParameter()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["weight"] = "1.5"
        };
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.Equal(1.5f, model.Weight);
    }

    [Fact]
    public async Task ShouldMapBooleanParameterTrue()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["sortDescending"] = "true",
            ["active"] = "1"
        };
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.True(model.SortDescending);
        Assert.True(model.Active);
    }

    [Fact]
    public async Task ShouldMapBooleanParameterFalse()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["sortDescending"] = "false",
            ["active"] = "0"
        };
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.False(model.SortDescending);
        Assert.False(model.Active);
    }

    [Fact]
    public async Task ShouldMapEnumParameter()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["state"] = "Error"
        };
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.Equal("Error", model.State);
    }

    [Fact]
    public async Task ShouldMapEnumParameterCaseInsensitive()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["state"] = "error"
        };
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.Equal("error", model.State);
    }

    [Fact]
    public async Task ShouldMapNullableParameterWithValue()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["count"] = "42"
        };
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.Equal(42, model.Count);
    }

    [Fact]
    public async Task ShouldMapNullableParameterWithoutValue()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>(); // No count parameter
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.Null(model.Count);
    }

    [Fact]
    public async Task ShouldIgnoreInvalidParameters()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["skip"] = "invalid",
            ["productCode"] = "ABC123" // This should still work
        };
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.Equal(0, model.Skip); // Default value, invalid ignored
        Assert.Equal("ABC123", model.ProductCode); // This should work
    }

    [Fact]
    public async Task ShouldBeCaseInsensitive()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["productcode"] = "ABC123" // Lowercase
        };
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.Equal("ABC123", model.ProductCode);
    }

    [Fact]
    public async Task ShouldIgnoreUnknownParameters()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["unknownParam"] = "value",
            ["productCode"] = "ABC123"
        };
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.Equal("ABC123", model.ProductCode);
        // unknownParam should be ignored (no exception)
    }

    [Fact]
    public async Task ShouldMapDateTimeParameter()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["createdDate"] = "2024-01-15"
        };
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.Equal(new DateTime(2024, 1, 15), model.CreatedDate);
    }

    [Fact]
    public async Task ShouldMapMultipleParameters()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["productCode"] = "ABC123",
            ["skip"] = "20",
            ["take"] = "50",
            ["sortDescending"] = "true",
            ["state"] = "Error"
        };
        var context = CreateBindingContext<TestRequest>(queryParams);

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.True(context.Result.IsModelSet);
        var model = context.Result.Model as TestRequest;
        Assert.NotNull(model);
        Assert.Equal("ABC123", model.ProductCode);
        Assert.Equal(20, model.Skip);
        Assert.Equal(50, model.Take);
        Assert.True(model.SortDescending);
        Assert.Equal("Error", model.State);
    }
}