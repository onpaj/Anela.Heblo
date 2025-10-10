# Dashboard Tiles Implementation Guide

This document provides a comprehensive guide for implementing new dashboard tiles in the Anela Heblo application.

## Architecture Overview

The dashboard tile system is built using Clean Architecture principles with clear separation of concerns:

- **Frontend**: React components with drag-and-drop functionality, TypeScript hooks for API integration
- **Backend**: Tile registry system, dashboard service, and tile implementations
- **Database**: User settings persistence with tile configurations

## Backend Implementation

### 1. Tile Interface and Base Classes

All tiles must implement the `ITile` interface:

### 2. Tile Size Enum

```csharp
public enum TileSize
{
    Small = 1,   // 1x1 grid cell
    Medium = 2,  // 2x1 grid cells
    Large = 3    // 2x2 grid cells
}
```


#### Step 1: Create the Tile Class

Location: `backend/src/Anela.Heblo.Application/Features/{Module}/Dashboard/{TileName}Tile.cs`

#### Step 2: Register the Tile

In your module's service registration:

```csharp
// In {Module}Module.cs or XccModule.cs
services.RegisterTile<{TileName}Tile>();
```

### 3. Tile Category Enum

Standard categories to maintain consistency are defined in enum `TileCategory`:


### 5. Data Structure Guidelines

#### Successful Response
```csharp
return new
{
    status = "success",
    data = actualData,
    metadata = new
    {
        lastUpdated = DateTime.UtcNow,
        source = "ServiceName"
    }
};
```

#### Error Response
```csharp
return new
{
    status = "error",
    error = "User-friendly error message",
    details = ex.Message // for debugging
};
```

#### Empty State Response
```csharp
return new
{
    status = "empty",
    message = "Žádná data k zobrazení",
    suggestion = "Přidejte data pomocí..."
};
```

## Frontend Implementation

### 1. Tile Content Components

Create tile-specific content components in `frontend/src/components/dashboard/tiles/content/`:


### 2. Register Tile Content

Add to `frontend/src/components/dashboard/tiles/TileContent.tsx`:

```typescript
import { {TileName}Content } from './content/{TileName}Content';

export const TileContent: React.FC<TileContentProps> = ({ tile }) => {
  switch (tile.tileId) {
    case '{tile-id}':
      return <{TileName}Content tile={tile} />;
    // ... other cases
    default:
      return <DefaultTileContent tile={tile} />;
  }
};
```

### 3. Common UI Components

Use these reusable components for consistency:

```typescript
// LoadingState
const LoadingState = () => (
  <div className="flex items-center justify-center h-full">
    <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
  </div>
);

// ErrorState
const ErrorState = ({ message }: { message: string }) => (
  <div className="flex items-center justify-center h-full text-center">
    <div>
      <div className="text-red-500 text-2xl mb-2">⚠️</div>
      <p className="text-red-600 text-sm">{message}</p>
    </div>
  </div>
);

// EmptyState
const EmptyState = ({ message, suggestion }: { message: string; suggestion?: string }) => (
  <div className="flex items-center justify-center h-full text-center">
    <div>
      <div className="text-gray-400 text-2xl mb-2">📊</div>
      <p className="text-gray-600 text-sm font-medium">{message}</p>
      {suggestion && (
        <p className="text-gray-500 text-xs mt-1">{suggestion}</p>
      )}
    </div>
  </div>
);
```

## Testing Guidelines

### 1. Backend Testing

Create tests in `backend/test/Anela.Heblo.Tests/Features/{Module}/Dashboard/`:

```csharp
public class {TileName}TileTests
{
    [Fact]
    public async Task GetDataAsync_WithValidUser_ReturnsExpectedData()
    {
        // Arrange
        var mockService = new Mock<I{Service}>();
        var tile = new {TileName}Tile(mockService.Object);
        
        // Act
        var result = await tile.GetDataAsync("test-user");
        
        // Assert
        Assert.NotNull(result);
        // Add specific assertions
    }

    [Fact]
    public async Task GetDataAsync_WithError_ReturnsErrorResponse()
    {
        // Test error handling
    }

    [Fact]
    public void TileProperties_HaveExpectedValues()
    {
        // Test static properties
        var tile = new {TileName}Tile(Mock.Of<I{Service}>());
        
        Assert.Equal("{tile-id}", tile.GetTileId());
        Assert.Equal("Expected Title", tile.Title);
        Assert.Equal(TileSize.Medium, tile.Size);
    }
}
```

### 2. Frontend Testing

Create component tests in `frontend/src/components/dashboard/tiles/content/__tests__/`:

```typescript
import { render, screen } from '@testing-library/react';
import { {TileName}Content } from '../{TileName}Content';

describe('{TileName}Content', () => {
  it('renders loading state when no data', () => {
    const tile = { tileId: '{tile-id}', data: null } as any;
    render(<{TileName}Content tile={tile} />);
    
    expect(screen.getByRole('status')).toBeInTheDocument();
  });

  it('renders error state when data has error', () => {
    const tile = { 
      tileId: '{tile-id}', 
      data: { status: 'error', error: 'Test error' } 
    } as any;
    
    render(<{TileName}Content tile={tile} />);
    
    expect(screen.getByText('Test error')).toBeInTheDocument();
  });

  it('renders content when data is available', () => {
    const tile = { 
      tileId: '{tile-id}', 
      data: { 
        status: 'success', 
        items: [{ id: 1, name: 'Test Item' }] 
      } 
    } as any;
    
    render(<{TileName}Content tile={tile} />);
    
    expect(screen.getByText('Test Item')).toBeInTheDocument();
  });
});
```

### 3. E2E Testing

Add Playwright tests in `frontend/test/e2e/dashboard-tiles.spec.ts`:

```typescript
test('should display {tile-name} tile with data', async ({ page }) => {
  await page.goto('/');
  await page.waitForSelector('[data-testid="dashboard-container"]');
  
  const tile = page.locator('[data-testid="dashboard-tile-{tile-id}"]');
  await expect(tile).toBeVisible();
  
  // Test specific tile content
  await expect(tile.locator('.tile-title')).toContainText('Expected Title');
});
```

## Performance Considerations

### 1. Backend Performance

- **Caching**: Implement caching for expensive operations
- **Async Operations**: Always use async/await for data fetching
- **Error Handling**: Implement proper exception handling
- **Resource Management**: Dispose of resources properly

```csharp
public async Task<object?> GetDataAsync(string userId)
{
    var cacheKey = $"tile:{GetTileId()}:user:{userId}";
    
    return await _cache.GetOrCreateAsync(cacheKey, async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
        return await FetchDataFromSource(userId);
    });
}
```

### 2. Frontend Performance

- **Memoization**: Use React.memo for expensive components
- **Lazy Loading**: Implement lazy loading for heavy tile content
- **Virtualization**: For tiles with large data sets

```typescript
const {TileName}Content = React.memo<{TileName}ContentProps>(({ tile }) => {
  // Component implementation
});
```

## Security Guidelines

### 1. Permission Checking

Always implement proper permission checking:

```csharp
public string[] RequiredPermissions => new[] { "dashboard.{module}.read" };

public async Task<object?> GetDataAsync(string userId)
{
    // Permission check is handled by the dashboard service
    // Focus on data-specific security here
    
    if (!await _authService.CanUserAccessData(userId, dataScope))
    {
        return new { status = "error", error = "Nedostatečná oprávnění" };
    }
    
    // Proceed with data fetching
}
```

### 2. Data Sanitization

Sanitize all data before returning:

```csharp
return new
{
    status = "success",
    items = data.Select(item => new
    {
        id = item.Id,
        name = _sanitizer.Sanitize(item.Name),
        // Only expose necessary fields
    })
};
```

## Configuration and Environment

### 1. Feature Flags

Support feature flags for gradual rollout:

```csharp
public bool DefaultEnabled => _featureFlags.IsEnabled("dashboard.{tile-id}");
```

### 2. Environment-Specific Behavior

```csharp
public async Task<object?> GetDataAsync(string userId)
{
    if (_environment.IsDevelopment())
    {
        // Return mock data or additional debug info
        return await GetMockDataAsync();
    }
    
    return await GetProductionDataAsync(userId);
}
```

## Monitoring and Logging

### 1. Logging

Implement comprehensive logging:

```csharp
public async Task<object?> GetDataAsync(string userId)
{
    using var scope = _logger.BeginScope("TileId: {TileId}, UserId: {UserId}", GetTileId(), userId);
    
    try
    {
        _logger.LogInformation("Fetching tile data");
        var data = await FetchData(userId);
        _logger.LogInformation("Successfully fetched {Count} items", data.Count);
        return data;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to fetch tile data");
        return new { status = "error", error = "Nepodařilo se načíst data" };
    }
}
```

### 2. Metrics

Track tile performance:

```csharp
using var activity = _diagnostics.StartActivity("TileDataFetch");
activity?.SetTag("tile.id", GetTileId());
activity?.SetTag("user.id", userId);
```

## Deployment Checklist

Before deploying a new tile:

### Backend
- [ ] Tile class implements `IDashboardTile`
- [ ] Tile is registered in service collection
- [ ] Unit tests are written and passing
- [ ] Integration tests cover the tile endpoint
- [ ] Permissions are properly configured
- [ ] Error handling is implemented
- [ ] Logging is added
- [ ] Performance is acceptable

### Frontend
- [ ] Tile content component is created
- [ ] Component is registered in `TileContent.tsx`
- [ ] Component tests are written and passing
- [ ] E2E tests cover the tile functionality
- [ ] Responsive design is implemented
- [ ] Loading and error states are handled
- [ ] Accessibility is considered

### Documentation
- [ ] Tile purpose and usage is documented
- [ ] API responses are documented
- [ ] Configuration options are documented
- [ ] Troubleshooting guide is provided

## Common Patterns and Examples

### 1. List Tile Pattern

For tiles displaying lists of items:

```csharp
return new
{
    status = "success",
    items = items.Select(item => new
    {
        id = item.Id,
        title = item.Title,
        subtitle = item.Subtitle,
        status = item.Status,
        priority = item.Priority,
        url = _urlHelper.Action("Details", "Controller", new { id = item.Id })
    }),
    totalCount = totalCount,
    hasMore = hasMore
};
```

### 2. Metric Tile Pattern

For tiles displaying key metrics:

```csharp
return new
{
    status = "success",
    metrics = new
    {
        primary = new { value = 42, label = "Celkem", trend = "+5%" },
        secondary = new { value = 8, label = "Dnes", trend = "-2%" }
    },
    chart = chartData, // Optional chart data
    period = "Tento týden"
};
```

### 3. Status Tile Pattern

For tiles showing system status:

```csharp
return new
{
    status = overallStatus, // "success", "warning", "error"
    services = services.Select(s => new
    {
        name = s.Name,
        status = s.Status,
        lastCheck = s.LastCheck,
        message = s.StatusMessage
    }),
    summary = $"{healthyCount}/{totalCount} services healthy"
};
```

This guide provides a comprehensive foundation for implementing dashboard tiles in the Anela Heblo application. Follow these patterns and guidelines to ensure consistency, maintainability, and scalability of the tile system.