# URL Parameter Mapping System

## Overview

The URL Parameter Mapping system enables automatic mapping of URL query parameters to request object properties in ASP.NET Core controllers. This allows for clean drill-down navigation where frontend URL parameters are automatically converted to backend request objects without manual parsing.

## Key Features

- **Automatic URL-to-Object Mapping**: URL query parameters automatically map to request object properties
- **Type-Safe Conversion**: Supports string, int, bool, enum, DateTime, and nullable types
- **Case-Insensitive Matching**: URL parameters match object properties regardless of casing
- **Opt-in Approach**: Uses `[UrlMappedQuery]` attribute to enable mapping only where needed
- **Drill-Down Navigation**: Enables seamless navigation from dashboard tiles to filtered list views

## Implementation Components

### 1. Custom Attribute (`UrlMappedQueryAttribute`)

```csharp
[AttributeUsage(AttributeTargets.Parameter)]
public class UrlMappedQueryAttribute : Attribute, IBindingSourceMetadata
{
    public BindingSource BindingSource => BindingSource.Query;
}
```

**Location**: `backend/src/Anela.Heblo.API/Infrastructure/UrlMappedQueryAttribute.cs`

### 2. Model Binder (`UrlMappingModelBinder`)

Handles the actual mapping of URL parameters to object properties with comprehensive type conversion.

**Key Features**:
- Maps all public writable properties
- Case-insensitive parameter matching
- Type conversion for primitives, enums, nullable types
- Proper ModelBindingResult for ASP.NET Core validation

**Location**: `backend/src/Anela.Heblo.API/Infrastructure/UrlMappingModelBinder.cs`

### 3. Model Binder Provider (`UrlMappingModelBinderProvider`)

Determines when to use the custom model binder. Currently configured for `GetTransportBoxesRequest`.

**Location**: `backend/src/Anela.Heblo.API/Infrastructure/UrlMappingModelBinderProvider.cs`

### 4. Registration in Program.cs

```csharp
options.ModelBinderProviders.Insert(0, new UrlMappingModelBinderProvider());
```

## Usage Example

### Backend Controller

```csharp
[HttpGet]
public async Task<ActionResult<GetTransportBoxesResponse>> GetTransportBoxes(
    [UrlMappedQuery] GetTransportBoxesRequest request,
    CancellationToken cancellationToken = default)
{
    var response = await _mediator.Send(request, cancellationToken);
    return Ok(response);
}
```

### Request Object

```csharp
public class GetTransportBoxesRequest : IRequest<GetTransportBoxesResponse>
{
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 50;
    public string? Code { get; set; }
    public string? State { get; set; }
    public string? ProductCode { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; }
}
```

### URL Examples

All these URLs automatically map to request object properties:

```
/api/transport-boxes?state=Error
/api/transport-boxes?state=InTransit&take=10
/api/transport-boxes?skip=20&take=50&sortBy=code&sortDescending=true
/api/transport-boxes?state=Error&productCode=ABC123&skip=0&take=25
```

### Frontend Integration

Frontend can read URL parameters and they automatically map to backend requests:

```typescript
// Frontend URL: /logistics/transport-boxes?state=Error
useEffect(() => {
  const state = searchParams.get('state');
  if (state) {
    setStateFilter(state); // This becomes the API parameter
  }
}, [searchParams]);
```

## Supported Type Conversions

- **String**: Direct assignment
- **Boolean**: `true/1` → true, `false/0` → false
- **Integers**: `int`, `long`, `short`
- **Decimals**: `double`, `float`, `decimal`
- **Enums**: Case-insensitive string-to-enum conversion
- **DateTime**: Standard DateTime parsing
- **Nullable Types**: All above types with null support
- **Fallback**: TypeConverter for other types

## How to Add URL Mapping to New Controllers

### Step 1: Update Model Binder Provider

Modify `UrlMappingModelBinderProvider.cs` to recognize your request type:

```csharp
public IModelBinder? GetBinder(ModelBinderProviderContext context)
{
    if (context.Metadata.ModelType.Name == "GetTransportBoxesRequest" ||
        context.Metadata.ModelType.Name == "YourNewRequestType")
    {
        return new UrlMappingModelBinder();
    }
    return null;
}
```

### Step 2: Add Attribute to Controller Method

```csharp
[HttpGet]
public async Task<ActionResult<YourResponse>> YourMethod(
    [UrlMappedQuery] YourRequestType request,
    CancellationToken cancellationToken = default)
{
    // Your implementation
}
```

### Step 3: Ensure Request Class Design

Make sure your request class follows these guidelines:

```csharp
public class YourRequestType : IRequest<YourResponseType>
{
    public string? FilterProperty { get; set; }
    public int PageSize { get; set; } = 20;
    public bool SomeFlag { get; set; }
    // All properties should be public with setters
}
```

## Frontend Drill-Down Navigation

### Dashboard Tiles

Backend tiles generate drill-down URLs:

```csharp
protected virtual string GenerateDrillDownUrl()
{
    return $"/logistics/transport-boxes?state={FilterStates[0]}";
}
```

### Frontend Navigation Utilities

Use consistent URL patterns:

```typescript
export const transportBoxUrls = {
  byState: (state: string) => `/logistics/transport-boxes?state=${state}`,
  errorBoxes: () => `/logistics/transport-boxes?state=Error`,
  inTransitBoxes: () => `/logistics/transport-boxes?state=InTransit`,
  withProduct: (productCode: string, state?: string) => 
    `/logistics/transport-boxes?productCode=${encodeURIComponent(productCode)}${state ? `&state=${state}` : ''}`,
};
```

### Frontend URL Parameter Reading

```typescript
const [searchParams] = useSearchParams();

useEffect(() => {
  const state = searchParams.get('state');
  const productCode = searchParams.get('productCode');
  const skip = searchParams.get('skip');
  
  if (state) setStateFilter(state);
  if (productCode) setProductFilter(productCode);
  if (skip) setSkip(parseInt(skip, 10));
}, [searchParams]);
```

## Benefits

1. **Clean URLs**: Bookmarkable, shareable URLs with meaningful parameters
2. **Type Safety**: Automatic conversion with error handling
3. **Maintainability**: Centralized mapping logic
4. **Consistency**: Uniform approach across all list controllers
5. **User Experience**: Direct navigation to filtered views from dashboard
6. **SEO Friendly**: Descriptive URLs for better search indexing

## Best Practices

1. **Parameter Naming**: Use consistent parameter names across controllers (`state`, `productCode`, `skip`, `take`)
2. **Default Values**: Set sensible defaults in request classes
3. **Validation**: Add validation attributes to request properties when needed
4. **Documentation**: Document supported parameters in controller XML comments
5. **Testing**: Test various URL parameter combinations
6. **Error Handling**: Graceful handling of invalid parameter values

## Example Use Cases

- **Dashboard Drill-Down**: Click "Error Boxes: 5" → Navigate to `/transport-boxes?state=Error`
- **Product Navigation**: Click transport count in inventory → Navigate to `/transport-boxes?productCode=ABC123&state=InTransit`
- **Bookmark/Share**: Users can bookmark and share filtered views
- **Deep Linking**: External systems can link directly to filtered data
- **Browser Navigation**: Back/forward buttons work correctly with filters

## Troubleshooting

### Common Issues

1. **Parameters Not Mapping**: Check ModelBinderProvider includes your request type
2. **Type Conversion Fails**: Ensure URL parameter format matches expected type
3. **Case Sensitivity**: System is case-insensitive, but verify parameter names
4. **Validation Errors**: Ensure `ModelBindingResult.Success()` is called correctly

### Debug Steps

1. Add temporary console logging to model binder
2. Check browser network tab for actual URL parameters
3. Verify request object property names match URL parameters
4. Test with simple parameter first, then add complexity

## Related Files

- `backend/src/Anela.Heblo.API/Infrastructure/UrlMappedQueryAttribute.cs`
- `backend/src/Anela.Heblo.API/Infrastructure/UrlMappingModelBinder.cs`
- `backend/src/Anela.Heblo.API/Infrastructure/UrlMappingModelBinderProvider.cs`
- `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs` (example usage)
- `frontend/src/components/pages/TransportBoxList.tsx` (frontend integration)
- `frontend/src/utils/drillDownNavigation.ts` (navigation utilities)