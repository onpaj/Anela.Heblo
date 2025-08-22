# 📊 Product Margin Summary Feature Specification

## 1. Feature Overview & Business Value

### Purpose
The Product Margin Summary feature provides business stakeholders with visual insights into how specific products contribute to overall profit margins over time, enabling data-driven decisions about product focus, pricing strategy, and inventory management.

### Business Value
- **Margin Analysis**: Understand which products generate the highest profit margins in absolute terms
- **Profitability Visibility**: See total profit contribution per product to identify most valuable items
- **Trend Analysis**: Track product margin performance across different time periods
- **Strategic Planning**: Make informed decisions about product portfolio optimization based on profitability
- **Resource Allocation**: Focus marketing and inventory efforts on highest-margin contributing products

### Target Users
- Business managers and executives
- Product managers
- Financial analysts
- Sales and marketing teams

---

## 2. Functional Requirements

### 2.1 Core Functionality

#### Graph Display
- **Chart Type**: Stacked column chart where each column represents monthly total profit margin (in CZK)
- **Column Composition**: Each column consists of colored segments representing top margin-contributing products
- **Product Limit**: Show top 7 products by total absolute margin, combine remaining products into "Other" category
- **Time Series**: Monthly aggregation with configurable time windows

#### Time Window Options
- **Current Year** (default): January to current month of current year
- **Current + Previous Year**: 24 months (current year + full previous year)
- **Last 6 Months**: Rolling 6-month window
- **Last 12 Months**: Rolling 12-month window  
- **Last 24 Months**: Rolling 24-month window

#### Product Filtering
- **Product Types**: Only include products with `ProductType.Product` (8) and `ProductType.Goods` (1)
- **Margin Calculation**: Based on `SalesHistory` records multiplied by `CatalogAggregate.Margin` (value margin per piece)
- **Top Product Logic**: Rank products by total absolute margin contribution across the selected time period

#### Interactive Features
- **Hover Tooltips**: Show detailed product information including:
  - Product name and code
  - Absolute margin per piece (from CatalogRepository)
  - Percentage margin per piece
  - Total units sold in the month
  - Selling price without VAT (EshopPrice)
  - Material cost (MaterialCosts from CatalogRepository)
  - Labor cost (LaborCosts from CatalogRepository)
  - Total margin contribution for the month
  - Percentage of monthly total margin
- **Legend**: Color-coded legend showing top 7 products + "Other" category
- **Responsive Design**: Adapt to different screen sizes maintaining readability

### 2.2 Data Requirements

#### Source Data
- **Sales History**: Use existing `CatalogAggregate.SalesHistory` (list of `CatalogSaleRecord`)
- **Margin Data**: Use `CatalogAggregate.Margin` (absolute value margin per piece in CZK)
- **Pricing Information**: `CatalogAggregate.EshopPrice.PriceWithoutVat` for display in tooltips
- **Cost Data**: `CatalogAggregate.MaterialCosts` and `CatalogAggregate.LaborCosts` for tooltip details
- **Product Types**: Filter by `CatalogAggregate.Type` (Product = 8, Goods = 1)
- **Product Information**: `ProductCode`, `ProductName` for display and identification

#### Margin Calculation Logic
```csharp
Monthly Margin = Sum of (SaleRecord.AmountB2B + SaleRecord.AmountB2C) * CatalogAggregate.Margin
Where SaleRecord.Date falls within the month and product matches criteria
```

#### Aggregation Rules
1. **Monthly Aggregation**: Group sales records by Year-Month
2. **Product Ranking**: Calculate total absolute margin per product across entire time window
3. **Top 7 Selection**: Select 7 highest margin-contributing products, combine rest into "Other"
4. **Color Assignment**: Assign consistent colors to top 7 products across all months

---

## 3. Technical Implementation

### 3.1 Backend Implementation (Vertical Slice Architecture)

#### Module Structure
```
backend/src/Anela.Heblo.Application/Features/Analytics/
├── contracts/
│   ├── GetProductMarginSummaryRequest.cs
│   ├── GetProductMarginSummaryResponse.cs
│   ├── ProductMarginSummaryDto.cs
│   └── MonthlyProductRevenueDto.cs
├── application/
│   ├── GetProductMarginSummaryHandler.cs
│   └── ProductMarginSummaryService.cs
├── domain/
│   ├── ProductMarginAnalysis.cs
│   └── RevenueCalculator.cs
└── AnalyticsModule.cs
```

#### API Endpoint
```csharp
[HttpGet("product-margin-summary")]
public async Task<ActionResult<GetProductMarginSummaryResponse>> GetProductMarginSummary(
    [FromQuery] GetProductMarginSummaryRequest request)
```

#### Request/Response Models
```csharp
public class GetProductMarginSummaryRequest : IRequest<GetProductMarginSummaryResponse>
{
    public string TimeWindow { get; set; } = "current-year"; // current-year, current-and-previous-year, last-6-months, last-12-months, last-24-months
    public int? TopProductCount { get; set; } = 7; // Configurable, default 7
}

public class GetProductMarginSummaryResponse
{
    public List<MonthlyProductMarginDto> MonthlyData { get; set; } = new();
    public List<TopProductDto> TopProducts { get; set; } = new(); // For legend/color mapping
    public decimal TotalMargin { get; set; }
    public string TimeWindow { get; set; } = string.Empty;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
}

public class MonthlyProductMarginDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthDisplay { get; set; } = string.Empty; // "Led 2024"
    public List<ProductMarginSegmentDto> ProductSegments { get; set; } = new();
    public decimal TotalMonthMargin { get; set; }
}

public class ProductMarginSegmentDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal MarginContribution { get; set; }; // Total margin for this product in this month
    public decimal Percentage { get; set; }; // Percentage of monthly total margin
    public string ColorCode { get; set; } = string.Empty; // Hex color for consistency
    public bool IsOther { get; set; } = false; // True for "Other" category
    
    // Tooltip detail information
    public decimal MarginPerPiece { get; set; }; // From CatalogAggregate.Margin
    public int UnitsSold { get; set; }; // Total units sold in this month
    public decimal SellingPriceWithoutVat { get; set; }; // From EshopPrice
    public decimal MaterialCosts { get; set; }; // From CatalogAggregate.MaterialCosts
    public decimal LaborCosts { get; set; }; // From CatalogAggregate.LaborCosts
}

public class TopProductDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal TotalMargin { get; set; }; // Total margin across entire time period
    public string ColorCode { get; set; } = string.Empty;
    public int Rank { get; set; }
}
```

#### Implementation Logic
```csharp
public class GetProductMarginSummaryHandler : IRequestHandler<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>
{
    public async Task<GetProductMarginSummaryResponse> Handle(GetProductMarginSummaryRequest request, CancellationToken cancellationToken)
    {
        // 1. Parse time window and calculate date range
        var (fromDate, toDate) = ParseTimeWindow(request.TimeWindow);
        
        // 2. Get all products with Product/Goods types that have sales in the period
        var products = await _catalogRepository.GetProductsWithSalesInPeriod(fromDate, toDate, 
            new[] { ProductType.Product, ProductType.Goods });
        
        // 3. Calculate total margin per product across entire period
        var productMarginMap = CalculateProductTotalMargin(products, fromDate, toDate);
        
        // 4. Get top N products by margin and assign colors
        var topProducts = GetTopProductsWithColors(productMarginMap, request.TopProductCount ?? 7);
        
        // 5. Generate monthly breakdown with product segments
        var monthlyData = GenerateMonthlyBreakdown(products, fromDate, toDate, topProducts);
        
        return new GetProductMarginSummaryResponse
        {
            MonthlyData = monthlyData,
            TopProducts = topProducts,
            TotalMargin = productMarginMap.Values.Sum(),
            TimeWindow = request.TimeWindow,
            FromDate = fromDate,
            ToDate = toDate
        };
    }
}
```

### 3.2 Frontend Implementation

#### Component Structure
```
frontend/src/components/pages/ProductMarginSummary.tsx
frontend/src/api/hooks/useProductMarginSummary.ts
```

#### Chart Implementation (Chart.js)
```tsx
import { Chart } from 'react-chartjs-2';
import { ChartOptions, ChartData } from 'chart.js';

const ProductMarginSummary: React.FC = () => {
  const [selectedTimeWindow, setSelectedTimeWindow] = useState<string>('current-year');
  
  const { data, isLoading, error } = useProductMarginSummary(selectedTimeWindow);
  
  const chartData = useMemo(() => {
    if (!data?.monthlyData) return null;
    
    const labels = data.monthlyData.map(m => m.monthDisplay);
    const datasets = data.topProducts.map(product => ({
      label: product.productName,
      data: data.monthlyData.map(month => 
        month.productSegments.find(s => s.productCode === product.productCode)?.marginContribution || 0
      ),
      backgroundColor: product.colorCode,
      borderColor: product.colorCode,
      borderWidth: 1,
    }));
    
    // Add "Other" category
    datasets.push({
      label: 'Ostatní produkty',
      data: data.monthlyData.map(month => 
        month.productSegments.find(s => s.isOther)?.marginContribution || 0
      ),
      backgroundColor: '#9CA3AF', // Gray for "Other"
      borderColor: '#9CA3AF',
      borderWidth: 1,
    });
    
    return { labels, datasets };
  }, [data]);
  
  const chartOptions: ChartOptions<'bar'> = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        position: 'right',
      },
      tooltip: {
        callbacks: {
          label: (context) => {
            const productName = context.dataset.label;
            const margin = formatCurrency(context.parsed.y);
            const monthData = data?.monthlyData[context.dataIndex];
            const segment = monthData?.productSegments.find(s => 
              s.productName === productName || (s.isOther && productName === 'Ostatní produkty')
            );
            const percentage = segment?.percentage || 0;
            
            // Enhanced tooltip with detailed margin information
            let tooltip = `${productName}: ${margin} (${percentage.toFixed(1)}%)`;
            if (segment && !segment.isOther) {
              tooltip += `\nMarže za kus: ${formatCurrency(segment.marginPerPiece)}`;
              tooltip += `\nProdej za kus: ${formatCurrency(segment.sellingPriceWithoutVat)}`;
              tooltip += `\nProdano kusů: ${segment.unitsSold}`;
              tooltip += `\nNáklady materiál: ${formatCurrency(segment.materialCosts)}`;
              tooltip += `\nNáklady práce: ${formatCurrency(segment.laborCosts)}`;
            }
            return tooltip;
          }
        }
      }
    },
    scales: {
      x: {
        stacked: true,
      },
      y: {
        stacked: true,
        ticks: {
          callback: (value) => formatCurrency(Number(value))
        },
        title: {
          display: true,
          text: 'Marže (Kč)'
        }
      }
    }
  };
  
  return (
    <div className="flex flex-col h-full w-full">
      {/* Header */}
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900">Přehled marží produktů</h1>
        <p className="mt-1 text-sm text-gray-600">
          Analýza celkové marže z prodeje produktů v čase s rozložením podle jednotlivých produktů
        </p>
      </div>
      
      {/* Time Window Selector */}
      <div className="flex-shrink-0 bg-white shadow rounded-lg p-4 mb-4">
        <div className="flex items-center space-x-4">
          <label htmlFor="time-window" className="text-sm font-medium text-gray-700">
            Časové období:
          </label>
          <select
            id="time-window"
            value={selectedTimeWindow}
            onChange={(e) => setSelectedTimeWindow(e.target.value)}
            className="block w-60 pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
          >
            <option value="current-year">Aktuální rok</option>
            <option value="current-and-previous-year">Aktuální + předchozí rok</option>
            <option value="last-6-months">Posledních 6 měsíců</option>
            <option value="last-12-months">Posledních 12 měsíců</option>
            <option value="last-24-months">Posledních 24 měsíců</option>
          </select>
        </div>
      </div>
      
      {/* Chart */}
      <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">
        {chartData && (
          <div className="flex-1 p-6">
            <div className="h-full">
              <Chart type="bar" data={chartData} options={chartOptions} />
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
```

### 3.3 Color Scheme Strategy

#### Product Color Palette
Use distinct, accessible colors for top 7 products:
```typescript
const PRODUCT_COLORS = [
  '#2563EB', // Primary Blue
  '#DC2626', // Red
  '#059669', // Green  
  '#D97706', // Orange
  '#7C3AED', // Purple
  '#DB2777', // Pink
  '#0891B2', // Cyan
];

const OTHER_COLOR = '#9CA3AF'; // Gray for "Other" category
```

#### Color Assignment Logic
- Assign colors based on product ranking (highest revenue gets first color)
- Maintain color consistency across all months for same product
- Use gray for "Other" category to visually distinguish it
- Ensure colors meet WCAG contrast requirements for accessibility

---

## 4. UI/UX Design Specification

### 4.1 Page Layout
Follow standard page layout structure from `layout_definition.md`:
- **Container**: `flex flex-col h-full w-full`
- **Header**: Fixed header with title and description (`flex-shrink-0 mb-3`)
- **Controls**: Time window selector in filter card (`flex-shrink-0 bg-white shadow rounded-lg p-4 mb-4`)
- **Chart**: Main content area with full height chart (`flex-1 bg-white shadow rounded-lg overflow-hidden`)

### 4.2 Chart Specifications

#### Dimensions & Spacing
- **Chart Height**: Minimum 400px on desktop, 350px on mobile
- **Legend Position**: Right side on desktop, top on mobile
- **Margins**: 16px padding inside chart container
- **Responsive**: Chart resizes with container, maintains aspect ratio

#### Visual Elements
- **Column Spacing**: 8px gap between columns
- **Border Radius**: Subtle rounded corners on chart segments
- **Hover Effects**: Highlight segment on hover with slight opacity change
- **Axis Labels**: Month names on X-axis, currency values on Y-axis
- **Grid Lines**: Subtle horizontal grid lines for value reference

### 4.3 Interactive Elements

#### Time Window Selector
- **Component**: Standard dropdown select
- **Position**: Top filter area
- **Width**: 240px (w-60)
- **Options**: 5 predefined time windows with Czech labels
- **Default**: Current year selected

#### Tooltips
- **Trigger**: Hover over chart segments
- **Content**: Product name, revenue amount, percentage of monthly total
- **Styling**: Dark background, white text, rounded corners
- **Position**: Dynamic positioning to avoid viewport edges

#### Legend
- **Items**: Product name with color indicator
- **Interaction**: Click to toggle product visibility
- **Responsive**: Switch to top position on mobile
- **Max Items**: 8 items (7 products + "Other")

### 4.4 Loading & Error States

#### Loading State
```tsx
<div className="flex items-center justify-center py-12">
  <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
  <span className="ml-2 text-gray-600">Načítám data o marži produktů...</span>
</div>
```

#### Error State
```tsx
<div className="mb-8 p-4 bg-red-50 border border-red-200 rounded-lg">
  <div className="flex items-center">
    <AlertTriangle className="w-5 h-5 text-red-500 mr-2" />
    <h3 className="text-red-800 font-medium">Chyba při načítání dat o marži</h3>
  </div>
  <p className="mt-1 text-red-700 text-sm">{error.message}</p>
</div>
```

#### Empty State
```tsx
<div className="text-center py-12">
  <BarChart3 className="mx-auto h-12 w-12 text-gray-400" />
  <h3 className="mt-2 text-sm font-medium text-gray-900">Žádná data o marži</h3>
  <p className="mt-1 text-sm text-gray-500">
    Pro vybrané období nejsou k dispozici žádná data o prodeji produktů.
  </p>
</div>
```

---

## 5. Integration with Existing Architecture

### 5.1 Vertical Slice Integration

#### Module Registration
```csharp
// In AnalyticsModule.cs
public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services)
    {
        services.AddScoped<IProductMarginSummaryService, ProductMarginSummaryService>();
        // ... other analytics services
        return services;
    }
}
```

#### Controller Integration
```csharp
[ApiController]
[Route("api/[controller]")]
public class AnalyticsController : ControllerBase
{
    [HttpGet("product-margin-summary")]
    public async Task<ActionResult<GetProductMarginSummaryResponse>> GetProductMarginSummary(
        [FromQuery] GetProductMarginSummaryRequest request)
    {
        var response = await _mediator.Send(request);
        return Ok(response);
    }
}
```

### 5.2 Data Access Integration

#### Repository Extension
Extend existing `ICatalogRepository` with new method:
```csharp
Task<List<CatalogAggregate>> GetProductsWithSalesInPeriod(
    DateTime fromDate, 
    DateTime toDate, 
    ProductType[] productTypes);
```

#### Query Optimization
- Use existing sales history summaries where possible
- Consider adding database indices for date-range queries on sales history
- Leverage existing `SaleHistorySummary.MonthlyData` for performance

### 5.3 Frontend Architecture Integration

#### Navigation Integration
Add new menu item to sidebar navigation:
```tsx
{
  name: 'Marže produktů',
  href: '/analytics/product-margin-summary',
  icon: BarChart3,
  section: 'analytics'
}
```

#### Route Configuration
```tsx
// In routing configuration
{
  path: '/analytics/product-margin-summary',
  element: <ProductMarginSummary />
}
```

#### API Hook Implementation
```tsx
// In api/hooks/useProductMarginSummary.ts
export const useProductMarginSummary = (timeWindow: string) => {
  return useQuery({
    queryKey: ['product-margin-summary', timeWindow],
    queryFn: () => apiClient.analytics.getProductMarginSummary({ timeWindow }),
    staleTime: 5 * 60 * 1000, // 5 minutes
  });
};
```

---

## 6. Testing Strategy

### 6.1 Backend Testing

#### Unit Tests
- `ProductMarginSummaryService` business logic
- Revenue calculation accuracy
- Time window parsing
- Product ranking and color assignment

#### Integration Tests
```csharp
[Test]
public async Task GetProductMarginSummary_CurrentYear_ReturnsCorrectData()
{
    // Arrange: Create test data with known sales history
    // Act: Call handler with current-year request
    // Assert: Verify monthly data, product ranking, revenue calculations
}
```

### 6.2 Frontend Testing

#### Component Tests (Jest + React Testing Library)
```tsx
describe('ProductMarginSummary', () => {
  test('renders chart with correct data', () => {
    // Test chart rendering with mock data
  });
  
  test('handles time window changes', () => {
    // Test dropdown interaction and API call triggering
  });
  
  test('displays loading state correctly', () => {
    // Test loading spinner and message
  });
});
```

#### UI Tests (Playwright)
```typescript
test('product margin summary displays and interacts correctly', async ({ page }) => {
  await page.goto('/analytics/product-margin-summary');
  
  // Wait for chart to load
  await page.waitForSelector('[data-testid="product-margin-chart"]');
  
  // Test time window selector
  await page.selectOption('select[id="time-window"]', 'last-12-months');
  
  // Verify chart updates
  await page.waitForLoadState('networkidle');
  
  // Test tooltip on hover
  await page.hover('.chartjs-bar:first-child');
  await expect(page.locator('.chartjs-tooltip')).toBeVisible();
});
```

---

## 7. Performance Considerations

### 7.1 Backend Optimization
- **Database Indexing**: Add indices on `SalesHistory.Date` and `CatalogAggregate.Type`
- **Caching Strategy**: Cache results for 15 minutes (reasonable for business reporting)
- **Query Optimization**: Use existing `SaleHistorySummary` monthly data when possible
- **Margin Calculation**: Pre-calculate margin values to avoid repetitive calculations
- **Pagination**: Not needed for chart data (limited to manageable dataset size)

### 7.2 Frontend Optimization
- **Chart Performance**: Use Chart.js with canvas rendering for good performance
- **Data Caching**: 5-minute stale time on React Query for API responses
- **Lazy Loading**: Load chart component only when route is accessed
- **Bundle Size**: Chart.js tree-shaking to include only required components

---

## 8. Future Enhancements

### 8.1 Advanced Features
- **Product Category Grouping**: Option to group products by category instead of individual products
- **Margin Percentage View**: Toggle between revenue view and margin percentage view
- **Export Functionality**: Export chart data to Excel/PDF
- **Drill-down Analysis**: Click on product segment to view detailed product analysis

### 8.2 Additional Metrics
- **Cost Analysis**: Include cost data to show actual profit margins
- **Comparison Features**: Compare current period with previous period
- **Forecasting**: Simple trend-based forecasting for future months
- **Product Lifecycle**: Track product performance across different lifecycle stages

### 8.3 User Experience
- **Custom Date Ranges**: Allow users to select custom date ranges
- **Saved Views**: Save and restore preferred time window settings
- **Dashboard Integration**: Add summary widget to main dashboard
- **Mobile Optimization**: Enhanced mobile chart interactions

---

## 9. Implementation Timeline

### Phase 1: Core Implementation (2-3 days)
- Backend API endpoint and data processing
- Basic frontend component with Chart.js
- Time window selection functionality
- Integration with existing navigation

### Phase 2: Polish & Testing (1-2 days)
- UI refinements and responsive design
- Error handling and loading states
- Comprehensive testing (unit + integration)
- Performance optimization

### Phase 3: Advanced Features (1-2 days)
- Enhanced tooltips and interactions
- Accessibility improvements
- Documentation and deployment
- User acceptance testing

**Total Estimated Effort**: 4-7 days

---

## 10. Acceptance Criteria

### Functional Criteria
- [x] Displays stacked column chart showing monthly product margins
- [x] Shows top 7 products by margin contribution with distinct colors
- [x] Combines remaining products into "Other" category  
- [x] Supports 5 different time window options
- [x] Only includes Product and Goods product types
- [x] Calculates margin using sales history and CatalogAggregate.Margin values
- [x] Provides interactive tooltips with detailed product margin information

### Technical Criteria
- [x] Follows vertical slice architecture pattern
- [x] Uses existing CatalogAggregate and SalesHistory data
- [x] Implements proper error handling and loading states
- [x] Follows established UI/UX design patterns
- [x] Includes comprehensive test coverage
- [x] Meets performance requirements (< 3 second load time)

### User Experience Criteria
- [x] Chart is responsive across different screen sizes
- [x] Color scheme is accessible and visually distinct
- [x] Loading and error states provide clear feedback
- [x] Time window changes update chart smoothly
- [x] Tooltips provide meaningful product information
- [x] Legend allows product visibility toggling

---

This specification provides a complete blueprint for implementing the Product Margin Summary feature while maintaining consistency with the existing architecture and design patterns of the Anela Heblo application.