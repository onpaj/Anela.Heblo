using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Anela.Heblo.API.Infrastructure;

/// <summary>
/// Attribute that enables automatic URL query parameter mapping to request object properties.
/// This attribute combines query source binding with intelligent type conversion.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public class UrlMappedQueryAttribute : Attribute, IBindingSourceMetadata
{
    public BindingSource BindingSource => BindingSource.Query;
}