# StockUpOperation Status Indicator Design

**Date:** 2026-01-21
**Feature:** Display pending/failed StockUpOperations on GiftPackageManufacturing UI
**Status:** Approved

## Overview

Add a status indicator to the GiftPackageManufacturing page that shows summary counts of pending/processing and failed StockUpOperations. This replaces the removed Hangfire job monitoring with database-backed operation tracking.

## Requirements

- Show count of operations "in queue" (Pending + Submitted states)
- Show count of failed operations
- Only display when there are active or failed operations
- Auto-refresh every 15 seconds
- Click to navigate to StockUpOperations management page with pre-applied filters
- Filter by SourceType.GiftPackageManufacture

## Backend Implementation

### New Endpoint

**Route:** `GET /api/stock-up-operations/summary?sourceType={sourceType}`

**Request:**
```csharp
public class GetStockUpOperationsSummaryRequest : IRequest<GetStockUpOperationsSummaryResponse>
{
    public StockUpSourceType? SourceType { get; set; } // Optional - omit for all types
}
```

**Response:**
```csharp
public class GetStockUpOperationsSummaryResponse : BaseResponse
{
    public int PendingCount { get; set; }
    public int SubmittedCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalInQueue => PendingCount + SubmittedCount;
}
```

**Controller:**
```csharp
// Location: Controllers/StockUpOperationsController.cs
[HttpGet("summary")]
public async Task<ActionResult<GetStockUpOperationsSummaryResponse>> GetSummary(
    [FromQuery] StockUpSourceType? sourceType = null)
{
    var request = new GetStockUpOperationsSummaryRequest
    {
        SourceType = sourceType
    };
    var response = await _mediator.Send(request);
    return Ok(response);
}
```

### Handler Implementation

**Location:** `Application/Features/Catalog/UseCases/GetStockUpOperationsSummary/`

**Files:**
- `GetStockUpOperationsSummaryRequest.cs`
- `GetStockUpOperationsSummaryResponse.cs`
- `GetStockUpOperationsSummaryHandler.cs`

**Query Logic:**
```csharp
var query = _repository.GetAll()
    .Where(x => x.State == Pending || x.State == Submitted || x.State == Failed);

if (request.SourceType.HasValue)
{
    query = query.Where(x => x.SourceType == request.SourceType.Value);
}

// Group by state and count efficiently
var counts = await query
    .GroupBy(x => x.State)
    .Select(g => new { State = g.Key, Count = g.Count() })
    .ToListAsync(cancellationToken);

// Map to response
return new GetStockUpOperationsSummaryResponse
{
    PendingCount = counts.FirstOrDefault(x => x.State == Pending)?.Count ?? 0,
    SubmittedCount = counts.FirstOrDefault(x => x.State == Submitted)?.Count ?? 0,
    FailedCount = counts.FirstOrDefault(x => x.State == Failed)?.Count ?? 0,
    Success = true
};
```

**Database Query:**
Single efficient query with GROUP BY:
```sql
SELECT State, COUNT(*)
FROM StockUpOperations
WHERE SourceType = @sourceType (if provided)
  AND State IN (Pending, Submitted, Failed)
GROUP BY State
```

## Frontend Implementation

### New Hook

**Location:** `frontend/src/api/hooks/useStockUpOperations.ts`

```typescript
export const useStockUpOperationsSummary = (sourceType?: StockUpSourceType) => {
  return useQuery({
    queryKey: ['stock-up-operations', 'summary', sourceType],
    queryFn: async (): Promise<GetStockUpOperationsSummaryResponse> => {
      const client = getAuthenticatedApiClient();
      return await client.stockUpOperations_GetSummary(sourceType);
    },
    refetchInterval: 15000, // Poll every 15 seconds
    staleTime: 10000, // Consider stale after 10 seconds
    gcTime: 30000, // Keep in cache for 30 seconds
  });
};
```

### Component Structure

**New Component:** `StockUpOperationStatusIndicator.tsx`
**Location:** `frontend/src/components/pages/GiftPackageManufacturing/`

```typescript
interface StockUpOperationStatusIndicatorProps {
  summary: GetStockUpOperationsSummaryResponse;
}

const StockUpOperationStatusIndicator: React.FC<StockUpOperationStatusIndicatorProps> = ({ summary }) => {
  const navigate = useNavigate();

  const handleClick = () => {
    // Navigate to stock-up operations page with filters
    navigate('/stock-up-operations?sourceType=GiftPackageManufacture&state=Pending,Submitted,Failed');
  };

  return (
    <div
      className="mb-4 p-4 bg-blue-50 rounded-lg border border-blue-200 cursor-pointer hover:bg-blue-100 transition-colors"
      onClick={handleClick}
    >
      <div className="flex items-center justify-between">
        <div className="flex items-center space-x-4">
          {summary.totalInQueue > 0 && (
            <div className="flex items-center space-x-2">
              <Loader2 className="h-5 w-5 text-blue-600 animate-spin" />
              <span className="text-sm font-medium text-blue-900">
                {summary.totalInQueue} operací ve frontě
              </span>
            </div>
          )}

          {summary.failedCount > 0 && (
            <div className="flex items-center space-x-2">
              <AlertTriangle className="h-5 w-5 text-red-600" />
              <span className="text-sm font-medium text-red-900">
                {summary.failedCount} selhalo
              </span>
            </div>
          )}
        </div>

        <ChevronRight className="h-5 w-5 text-gray-400" />
      </div>
    </div>
  );
};

export default StockUpOperationStatusIndicator;
```

### Integration

**Location:** `frontend/src/components/pages/GiftPackageManufacturing/index.tsx`

```typescript
import StockUpOperationStatusIndicator from './StockUpOperationStatusIndicator';
import { useStockUpOperationsSummary } from '../../../api/hooks/useStockUpOperations';
import { StockUpSourceType } from '../../../api/generated/api-client';

const GiftPackageManufacturing: React.FC = () => {
  // Existing state...

  // Add summary hook
  const { data: stockUpSummary } = useStockUpOperationsSummary(
    StockUpSourceType.GiftPackageManufacture
  );

  // Conditionally show indicator
  const showIndicator = stockUpSummary &&
    (stockUpSummary.totalInQueue > 0 || stockUpSummary.failedCount > 0);

  return (
    <>
      {showIndicator && (
        <StockUpOperationStatusIndicator summary={stockUpSummary} />
      )}

      <GiftPackageManufacturingList
        onPackageClick={handlePackageClick}
        // ... other props
      />

      {/* Existing modals... */}
    </>
  );
};
```

## Visual Design

### Display States

**When there are operations in queue:**
- Blue background (`bg-blue-50`)
- Spinning loader icon (`Loader2` with `animate-spin`)
- Text: "{count} operací ve frontě"

**When there are failed operations:**
- Red alert icon (`AlertTriangle`)
- Text: "{count} selhalo"

**Both conditions:**
- Show both indicators side by side with space-x-4
- Separated visually with distinct icons and colors

**Hover state:**
- Background changes to `bg-blue-100`
- Cursor shows pointer
- Right chevron indicates clickable

**Hidden state:**
- Component not rendered when `totalInQueue === 0 && failedCount === 0`

### Color Scheme

- **Container:** Blue theme - `bg-blue-50`, `border-blue-200`
- **Hover:** `hover:bg-blue-100`
- **In queue text:** `text-blue-900`, icon `text-blue-600`
- **Failed text:** `text-red-900`, icon `text-red-600`
- **Chevron:** `text-gray-400`

### Placement

- Displayed at the top of the GiftPackageManufacturing page
- Above the filters section
- `mb-4` spacing from content below
- Full width of the page container

## Navigation

**Target:** `/stock-up-operations`

**Query Parameters:**
- `sourceType=GiftPackageManufacture` - filter by source type
- `state=Pending,Submitted,Failed` - show only active/failed operations

**Note:** The StockUpOperations management page should support these query parameters for filtering.

## Auto-refresh Behavior

- **Polling interval:** 15 seconds
- **Stale time:** 10 seconds (React Query considers data stale after 10s)
- **Cache time:** 30 seconds (keep data in cache for 30s after unmount)
- **No pause on failure:** Continue polling even if requests fail
- **Automatic on mount:** Starts polling immediately when component mounts
- **Stops on unmount:** Polling stops when user navigates away

## State Definitions

**"In Queue" includes:**
- `StockUpOperationState.Pending` (0) - Created, waiting to be sent
- `StockUpOperationState.Submitted` (1) - Sent to Shoptet, waiting for verification

**"Failed" includes:**
- `StockUpOperationState.Failed` (3) - Failed, requires manual review

**Excluded from counts:**
- `StockUpOperationState.Completed` (2) - Successfully finished

## Dependencies

**Backend:**
- MediatR for request/response pattern
- Entity Framework Core for database queries
- AutoMapper (if mapping is needed)
- Existing `IStockUpOperationRepository`

**Frontend:**
- React Query (`@tanstack/react-query`) for data fetching
- React Router (`useNavigate`) for navigation
- Lucide React icons (`Loader2`, `AlertTriangle`, `ChevronRight`)
- Tailwind CSS for styling
- Generated OpenAPI client for API calls

## Implementation Order

1. **Backend:**
   - Create request/response DTOs
   - Implement handler with repository query
   - Add controller endpoint
   - Test with Postman/Swagger

2. **Frontend:**
   - Wait for OpenAPI client regeneration
   - Create hook `useStockUpOperationsSummary`
   - Create `StockUpOperationStatusIndicator` component
   - Integrate into `GiftPackageManufacturing/index.tsx`
   - Test with real data

3. **Verification:**
   - Backend build passes
   - Frontend build passes
   - Manual testing with pending/failed operations
   - Verify polling works (15s refresh)
   - Verify navigation to StockUpOperations page

## Success Criteria

- ✅ Summary endpoint returns correct counts
- ✅ Indicator appears only when there are active/failed operations
- ✅ Auto-refreshes every 15 seconds
- ✅ Clicking navigates to StockUpOperations page with correct filters
- ✅ Visual design matches specification
- ✅ No performance degradation from polling
- ✅ Backend build passes
- ✅ Frontend build passes
