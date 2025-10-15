using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace Anela.Heblo.API.Infrastructure;

/// <summary>
/// Model binder that automatically maps URL query parameters to request object properties
/// with intelligent type conversion.
/// </summary>
public class UrlMappingModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (bindingContext == null)
            throw new ArgumentNullException(nameof(bindingContext));

        // Create instance of the model type
        var modelType = bindingContext.ModelType;
        var model = Activator.CreateInstance(modelType);

        if (model == null)
        {
            return Task.CompletedTask;
        }

        var request = bindingContext.HttpContext.Request;
        var queryParams = request.Query;

        // Get all properties of the model type
        var properties = modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToArray();

        foreach (var property in properties)
        {
            // Try to find query parameter (case-insensitive)
            var queryKey = queryParams.Keys.FirstOrDefault(k => 
                string.Equals(k, property.Name, StringComparison.OrdinalIgnoreCase));

            if (queryKey != null && queryParams.TryGetValue(queryKey, out var values))
            {
                var value = values.FirstOrDefault();
                if (!string.IsNullOrEmpty(value))
                {
                    var convertedValue = ConvertValue(value, property.PropertyType);
                    if (convertedValue != null)
                    {
                        property.SetValue(model, convertedValue);
                    }
                }
            }
        }

        // Set the model and mark binding as successful
        bindingContext.Model = model;
        bindingContext.Result = ModelBindingResult.Success(model);
        
        return Task.CompletedTask;
    }

    private static object? ConvertValue(string value, Type targetType)
    {
        try
        {
            // Handle nullable types
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            var typeToConvert = underlyingType ?? targetType;

            // String - direct assignment
            if (typeToConvert == typeof(string))
                return value;

            // Boolean conversion
            if (typeToConvert == typeof(bool))
            {
                return value.ToLowerInvariant() switch
                {
                    "true" or "1" => true,
                    "false" or "0" => false,
                    _ => bool.TryParse(value, out var boolResult) ? boolResult : null
                };
            }

            // Enum conversion
            if (typeToConvert.IsEnum)
            {
                return Enum.TryParse(typeToConvert, value, true, out var enumResult) ? enumResult : null;
            }

            // Numeric conversions
            if (typeToConvert == typeof(int))
                return int.TryParse(value, out var intResult) ? intResult : null;

            if (typeToConvert == typeof(long))
                return long.TryParse(value, out var longResult) ? longResult : null;

            if (typeToConvert == typeof(short))
                return short.TryParse(value, out var shortResult) ? shortResult : null;

            if (typeToConvert == typeof(double))
                return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleResult) ? doubleResult : null;

            if (typeToConvert == typeof(float))
                return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatResult) ? floatResult : null;

            if (typeToConvert == typeof(decimal))
                return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalResult) ? decimalResult : null;

            // DateTime conversion
            if (typeToConvert == typeof(DateTime))
                return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateResult) ? dateResult : null;

            // Fallback to TypeConverter
            var converter = TypeDescriptor.GetConverter(typeToConvert);
            if (converter.CanConvertFrom(typeof(string)))
            {
                return converter.ConvertFromString(null, CultureInfo.InvariantCulture, value);
            }

            return null;
        }
        catch
        {
            // Ignore conversion errors and return null
            return null;
        }
    }
}