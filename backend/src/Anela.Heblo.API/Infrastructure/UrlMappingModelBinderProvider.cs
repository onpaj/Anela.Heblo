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

        // Only use our custom model binder for GetTransportBoxesRequest
        if (context.Metadata.ModelType.Name == "GetTransportBoxesRequest")
        {
            return new UrlMappingModelBinder();
        }

        return null;
    }
}