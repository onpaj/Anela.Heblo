using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Reflection;

namespace Anela.Heblo.API.Infrastructure;

/// <summary>
/// Model binder provider that creates URL mapping model binders for parameters marked with [UrlMappedQuery].
/// </summary>
public class UrlMappingModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        // Check if the parameter is decorated with UrlMappedQueryAttribute
        // We need to check the actual parameter attributes since UrlMappedQueryAttribute
        // now uses standard BindingSource.Query
        if (context.BindingInfo?.BindingSource == BindingSource.Query)
        {
            // Check if this parameter has the UrlMappedQueryAttribute
            // We can check the metadata for the parameter attributes
            var parameterDescriptor = context.Metadata as Microsoft.AspNetCore.Mvc.ModelBinding.Metadata.DefaultModelMetadata;
            
            if (parameterDescriptor?.Attributes?.Attributes != null)
            {
                var hasUrlMappedQueryAttribute = parameterDescriptor.Attributes.Attributes
                    .OfType<UrlMappedQueryAttribute>()
                    .Any();

                if (hasUrlMappedQueryAttribute)
                {
                    // Additional safety check - only use our binder for complex types
                    var modelType = context.Metadata.ModelType;
                    if (!IsSimpleType(modelType))
                    {
                        return new UrlMappingModelBinder();
                    }
                }
            }
        }

        return null;
    }

    private static bool IsSimpleType(Type type)
    {
        // Consider nullable types
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        
        return underlyingType.IsPrimitive ||
               underlyingType.IsEnum ||
               underlyingType == typeof(string) ||
               underlyingType == typeof(DateTime) ||
               underlyingType == typeof(DateTimeOffset) ||
               underlyingType == typeof(TimeSpan) ||
               underlyingType == typeof(Guid) ||
               underlyingType == typeof(decimal);
    }
}