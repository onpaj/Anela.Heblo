# Journal Feature - Test Scenarios

## Overview

This document defines comprehensive test scenarios for the Journal feature across all three implementation phases. Tests cover unit testing, integration testing, and UI/UX validation.

## Test Categories

- **Unit Tests (UT)**: Backend logic and data operations
- **Integration Tests (IT)**: API endpoints and cross-module functionality  
- **UI Tests (UI)**: User interface and user experience validation
- **E2E Tests (E2E)**: Complete user workflows across modules

## Phase 1: Standalone Journal Management

### Test Scenarios - Journal Entry CRUD Operations

#### UT-J1.1: Create Journal Entry
**Test Case**: Creating a new journal entry with all fields
```csharp
[Test]
public async Task CreateJournalEntry_WithValidData_ShouldSucceed()
{
    // Given: Valid journal entry data with product associations
    var request = new CreateJournalEntryRequest
    {
        Title = "Production Issue Report",
        Content = "Found quality issue with batch #123",
        EntryDate = DateTime.Today,
        AssociatedProductCodes = new[] { "PROD-001", "PROD-002" },
        AssociatedProductFamilies = new[] { "CREAM-" },
        Tags = new[] { "quality", "production" }
    };
    
    // When: Creating the entry
    var result = await handler.Handle(request, CancellationToken.None);
    
    // Then: Entry should be created successfully
    result.Should().NotBeNull();
    result.Id.Should().BeGreaterThan(0);
    result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
}
```

#### UT-J1.2: Create Journal Entry Validation
**Test Case**: Validate required fields and data constraints
```csharp
[Test]
public async Task CreateJournalEntry_WithEmptyContent_ShouldFail()
{
    // Given: Entry with empty content
    var request = new CreateJournalEntryRequest
    {
        Title = "Test Entry",
        Content = "", // Empty content should fail
        EntryDate = DateTime.Today
    };
    
    // When/Then: Should throw validation exception
    await FluentActions.Invoking(() => handler.Handle(request, CancellationToken.None))
        .Should().ThrowAsync<ValidationException>()
        .WithMessage("*Content is required*");
}
```

#### IT-J1.3: Journal Entry API Integration
**Test Case**: Full API workflow for journal entry creation
```csharp
[Test]
public async Task JournalApi_CreateAndRetrieve_ShouldWork()
{
    // Given: API client and valid entry data
    var createRequest = new CreateJournalEntryRequest
    {
        Title = "API Test Entry",
        Content = "Testing API integration",
        EntryDate = DateTime.Today.AddDays(-1),
        AssociatedProductCodes = new[] { "TEST-001" }
    };
    
    // When: Creating via API
    var createResponse = await apiClient.Journal.CreateAsync(createRequest);
    
    // Then: Should create successfully
    createResponse.Should().NotBeNull();
    var entryId = createResponse.Id;
    
    // When: Retrieving created entry
    var retrieveResponse = await apiClient.Journal.GetAsync(entryId);
    
    // Then: Should match created data
    retrieveResponse.Title.Should().Be(createRequest.Title);
    retrieveResponse.Content.Should().Be(createRequest.Content);
    retrieveResponse.AssociatedProductCodes.Should().Contain("TEST-001");
}
```

### Test Scenarios - Search and Filtering

#### UT-J1.4: Full-Text Search
**Test Case**: Search journal entries by content
```csharp
[Test]
public async Task SearchJournalEntries_ByContent_ShouldReturnMatches()
{
    // Given: Multiple entries with different content
    await CreateTestEntry("Quality issue with cream production", "CREAM-001");
    await CreateTestEntry("Packaging update completed", "PACK-001");
    await CreateTestEntry("New cream formula approved", "CREAM-002");
    
    var request = new SearchJournalEntriesRequest
    {
        SearchText = "cream",
        PageSize = 10
    };
    
    // When: Searching for "cream"
    var result = await searchHandler.Handle(request, CancellationToken.None);
    
    // Then: Should return 2 cream-related entries
    result.Entries.Should().HaveCount(2);
    result.Entries.All(e => e.Content.ToLower().Contains("cream")).Should().BeTrue();
}
```

#### UT-J1.5: Date Range Filtering
**Test Case**: Filter entries by date range
```csharp
[Test]
public async Task SearchJournalEntries_ByDateRange_ShouldFilterCorrectly()
{
    // Given: Entries across different dates
    await CreateTestEntry("Old entry", DateTime.Today.AddDays(-30));
    await CreateTestEntry("Recent entry", DateTime.Today.AddDays(-5));
    await CreateTestEntry("Today entry", DateTime.Today);
    
    var request = new SearchJournalEntriesRequest
    {
        DateFrom = DateTime.Today.AddDays(-7),
        DateTo = DateTime.Today,
        PageSize = 10
    };
    
    // When: Filtering by last 7 days
    var result = await searchHandler.Handle(request, CancellationToken.None);
    
    // Then: Should return only recent entries
    result.Entries.Should().HaveCount(2);
    result.Entries.All(e => e.EntryDate >= DateTime.Today.AddDays(-7)).Should().BeTrue();
}
```

#### UT-J1.6: Product Family Association
**Test Case**: Associate entries with product families
```csharp
[Test]
public async Task CreateJournalEntry_WithProductFamily_ShouldAssociateCorrectly()
{
    // Given: Entry with product family association
    var request = new CreateJournalEntryRequest
    {
        Title = "Cream line quality update",
        Content = "All cream products quality improved",
        EntryDate = DateTime.Today,
        AssociatedProductFamilies = new[] { "CREAM-", "LOTION-" }
    };
    
    // When: Creating entry
    var result = await handler.Handle(request, CancellationToken.None);
    
    // Then: Should associate with product families
    var entry = await GetJournalEntry(result.Id);
    entry.AssociatedProductFamilies.Should().HaveCount(2);
    entry.AssociatedProductFamilies.Select(pf => pf.ProductCodePrefix)
        .Should().Contain(new[] { "CREAM-", "LOTION-" });
}
```

### Test Scenarios - UI/UX Validation

#### UI-J1.7: Journal List Page Display
**Test Case**: Verify journal list page layout and functionality
```javascript
test('Journal list page should display entries correctly', async ({ page }) => {
    // Given: Navigate to journal page
    await page.goto('/journal');
    await expect(page).toHaveTitle(/Journal/);
    
    // Then: Should display journal entries
    await expect(page.locator('[data-testid="journal-entry"]')).toBeVisible();
    
    // And: Should show search box
    await expect(page.locator('[data-testid="journal-search"]')).toBeVisible();
    
    // And: Should show add entry button
    await expect(page.locator('[data-testid="add-journal-entry"]')).toBeVisible();
});
```

#### UI-J1.8: Create Journal Entry Form
**Test Case**: Validate entry creation form functionality
```javascript
test('Should create new journal entry successfully', async ({ page }) => {
    // Given: Navigate to journal and click add
    await page.goto('/journal');
    await page.click('[data-testid="add-journal-entry"]');
    
    // When: Fill out entry form
    await page.fill('[data-testid="entry-title"]', 'Test Entry');
    await page.fill('[data-testid="entry-content"]', 'This is a test entry content');
    await page.selectOption('[data-testid="entry-date"]', '2024-01-15');
    
    // And: Associate with products
    await page.click('[data-testid="product-association"]');
    await page.fill('[data-testid="product-search"]', 'CREAM-001');
    await page.click('[data-testid="product-CREAM-001"]');
    
    // And: Submit form
    await page.click('[data-testid="save-entry"]');
    
    // Then: Should show success message
    await expect(page.locator('[data-testid="success-message"]')).toContainText('Entry created successfully');
    
    // And: Should return to journal list
    await expect(page).toHaveURL('/journal');
    
    // And: Should show new entry in list
    await expect(page.locator('[data-testid="journal-entry"]').first()).toContainText('Test Entry');
});
```

#### UI-J1.9: Search Functionality
**Test Case**: Validate search and filtering capabilities
```javascript
test('Should search and filter journal entries', async ({ page }) => {
    // Given: Navigate to journal page
    await page.goto('/journal');
    
    // When: Enter search term
    await page.fill('[data-testid="journal-search"]', 'quality');
    await page.press('[data-testid="journal-search"]', 'Enter');
    
    // Then: Should filter results
    await page.waitForLoadState('networkidle');
    const entries = page.locator('[data-testid="journal-entry"]');
    await expect(entries).toHaveCount(await entries.count());
    
    // And: Each visible entry should contain search term
    const entryTexts = await entries.allTextContents();
    entryTexts.forEach(text => {
        expect(text.toLowerCase()).toContain('quality');
    });
    
    // When: Clear search
    await page.fill('[data-testid="journal-search"]', '');
    await page.press('[data-testid="journal-search"]', 'Enter');
    
    // Then: Should show all entries again
    await page.waitForLoadState('networkidle');
    await expect(entries.first()).toBeVisible();
});
```

## Phase 2: Catalog Integration

### Test Scenarios - Product-Specific Entries

#### IT-J2.1: Get Entries by Product
**Test Case**: Retrieve journal entries associated with specific product
```csharp
[Test]
public async Task GetJournalEntriesByProduct_ShouldReturnAssociatedEntries()
{
    // Given: Journal entries with product associations
    await CreateTestEntry("Direct product entry", productCodes: new[] { "CREAM-001" });
    await CreateTestEntry("Family product entry", productFamilies: new[] { "CREAM-" });
    await CreateTestEntry("Unrelated entry", productCodes: new[] { "SOAP-001" });
    
    var request = new GetJournalEntriesByProductRequest
    {
        ProductCode = "CREAM-001"
    };
    
    // When: Getting entries for CREAM-001
    var result = await handler.Handle(request, CancellationToken.None);
    
    // Then: Should return both direct and family associations
    result.Entries.Should().HaveCount(2);
    result.Entries.Should().Contain(e => e.Title == "Direct product entry");
    result.Entries.Should().Contain(e => e.Title == "Family product entry");
}
```

#### UI-J2.2: Catalog Detail Journal Section
**Test Case**: Display journal entries on product detail page
```javascript
test('Product detail should show associated journal entries', async ({ page }) => {
    // Given: Navigate to product detail page
    await page.goto('/catalog/CREAM-001');
    
    // Then: Should display journal section
    await expect(page.locator('[data-testid="journal-section"]')).toBeVisible();
    
    // And: Should show associated entries
    const journalEntries = page.locator('[data-testid="product-journal-entry"]');
    await expect(journalEntries).toHaveCountGreaterThan(0);
    
    // And: Should display entry preview
    await expect(journalEntries.first()).toContainText('Direct product entry');
    
    // When: Click to expand entry
    await journalEntries.first().click();
    
    // Then: Should show full content
    await expect(page.locator('[data-testid="journal-entry-full-content"]')).toBeVisible();
});
```

#### UI-J2.3: Quick Add Journal Entry from Product
**Test Case**: Create journal entry directly from product detail page
```javascript
test('Should create journal entry from product detail page', async ({ page }) => {
    // Given: Navigate to product detail
    await page.goto('/catalog/CREAM-001');
    
    // When: Click add journal entry
    await page.click('[data-testid="add-journal-from-product"]');
    
    // Then: Should open quick add form
    await expect(page.locator('[data-testid="quick-journal-form"]')).toBeVisible();
    
    // And: Product should be pre-selected
    await expect(page.locator('[data-testid="associated-products"]')).toContainText('CREAM-001');
    
    // When: Fill and submit form
    await page.fill('[data-testid="quick-entry-content"]', 'Quick entry from product page');
    await page.click('[data-testid="save-quick-entry"]');
    
    // Then: Should show success and refresh journal section
    await expect(page.locator('[data-testid="success-message"]')).toBeVisible();
    await expect(page.locator('[data-testid="product-journal-entry"]').first())
        .toContainText('Quick entry from product page');
});
```

### Test Scenarios - Journal Indicators

#### UT-J2.4: Journal Indicator Logic
**Test Case**: Calculate journal indicators for products
```csharp
[Test]
public async Task GetJournalIndicators_ForProductList_ShouldReturnCorrectIndicators()
{
    // Given: Products with various journal associations
    await CreateTestEntry("Entry 1", productCodes: new[] { "CREAM-001" });
    await CreateTestEntry("Entry 2", productCodes: new[] { "CREAM-001", "CREAM-002" });
    await CreateTestEntry("Entry 3", productFamilies: new[] { "CREAM-" });
    
    var productCodes = new[] { "CREAM-001", "CREAM-002", "SOAP-001" };
    var request = new GetJournalIndicatorsRequest
    {
        ProductCodes = productCodes
    };
    
    // When: Getting indicators
    var result = await handler.Handle(request, CancellationToken.None);
    
    // Then: Should return correct counts
    result.Indicators.Should().HaveCount(3);
    result.Indicators["CREAM-001"].DirectEntries.Should().Be(2);
    result.Indicators["CREAM-001"].FamilyEntries.Should().Be(1);
    result.Indicators["CREAM-002"].DirectEntries.Should().Be(1);
    result.Indicators["CREAM-002"].FamilyEntries.Should().Be(1);
    result.Indicators["SOAP-001"].DirectEntries.Should().Be(0);
    result.Indicators["SOAP-001"].FamilyEntries.Should().Be(0);
}
```

## Phase 3: Cross-Module Integration

### Test Scenarios - Dashboard Integration

#### UI-J3.1: Dashboard Journal Widget
**Test Case**: Display recent journal activity on dashboard
```javascript
test('Dashboard should show recent journal activity', async ({ page }) => {
    // Given: Navigate to dashboard
    await page.goto('/dashboard');
    
    // Then: Should display journal widget
    await expect(page.locator('[data-testid="journal-widget"]')).toBeVisible();
    
    // And: Should show recent entries
    const recentEntries = page.locator('[data-testid="recent-journal-entry"]');
    await expect(recentEntries).toHaveCountGreaterThan(0);
    
    // And: Should show entry dates
    await expect(recentEntries.first().locator('[data-testid="entry-date"]')).toBeVisible();
    
    // When: Click on entry
    await recentEntries.first().click();
    
    // Then: Should navigate to full journal view
    await expect(page).toHaveURL(/\/journal/);
});
```

### Test Scenarios - Cross-Module Indicators

#### UI-J3.2: Journal Indicators in Product Lists
**Test Case**: Show journal indicators across different product views
```javascript
test('Product lists should show journal indicators', async ({ page }) => {
    // Given: Navigate to catalog list
    await page.goto('/catalog');
    
    // Then: Products with journal entries should show indicators
    const productsWithJournal = page.locator('[data-testid="product-row"][data-has-journal="true"]');
    await expect(productsWithJournal).toHaveCountGreaterThan(0);
    
    // And: Should show journal icon
    await expect(productsWithJournal.first().locator('[data-testid="journal-indicator"]')).toBeVisible();
    
    // When: Hover over indicator
    await productsWithJournal.first().locator('[data-testid="journal-indicator"]').hover();
    
    // Then: Should show tooltip with entry count
    await expect(page.locator('[data-testid="journal-tooltip"]')).toContainText('journal entries');
});
```

#### UI-J3.3: Contextual Journal Creation
**Test Case**: Create journal entries from different modules
```javascript
test('Should create journal entry from purchase analysis page', async ({ page }) => {
    // Given: Navigate to purchase analysis
    await page.goto('/purchase/analysis');
    
    // When: Right-click on product row
    await page.click('[data-testid="product-CREAM-001"]', { button: 'right' });
    
    // Then: Should show context menu with journal option
    await expect(page.locator('[data-testid="context-menu"]')).toBeVisible();
    await expect(page.locator('[data-testid="add-to-journal"]')).toBeVisible();
    
    // When: Click add to journal
    await page.click('[data-testid="add-to-journal"]');
    
    // Then: Should open journal form with context
    await expect(page.locator('[data-testid="contextual-journal-form"]')).toBeVisible();
    await expect(page.locator('[data-testid="context-info"]')).toContainText('Purchase Analysis');
    await expect(page.locator('[data-testid="associated-products"]')).toContainText('CREAM-001');
});
```

## Performance Test Scenarios

#### PERF-J1: Large Dataset Search Performance
**Test Case**: Validate search performance with large number of entries
```csharp
[Test]
public async Task SearchJournalEntries_WithLargeDataset_ShouldPerformWell()
{
    // Given: Create 10,000 journal entries
    var entries = GenerateTestEntries(10000);
    await BulkCreateEntries(entries);
    
    var request = new SearchJournalEntriesRequest
    {
        SearchText = "performance test",
        PageSize = 20
    };
    
    // When: Performing search
    var stopwatch = Stopwatch.StartNew();
    var result = await searchHandler.Handle(request, CancellationToken.None);
    stopwatch.Stop();
    
    // Then: Should complete within reasonable time
    stopwatch.ElapsedMilliseconds.Should().BeLessThan(500); // 500ms threshold
    result.Entries.Should().HaveCount(20);
    result.TotalCount.Should().BeGreaterThan(0);
}
```

#### PERF-J2: Product Association Query Performance
**Test Case**: Validate performance of product association queries
```csharp
[Test]
public async Task GetJournalEntriesByProduct_WithManyAssociations_ShouldPerformWell()
{
    // Given: Create entries with complex product associations
    await CreateEntriesWithProductAssociations(1000);
    
    var request = new GetJournalEntriesByProductRequest
    {
        ProductCode = "CREAM-001"
    };
    
    // When: Getting entries by product
    var stopwatch = Stopwatch.StartNew();
    var result = await handler.Handle(request, CancellationToken.None);
    stopwatch.Stop();
    
    // Then: Should complete quickly
    stopwatch.ElapsedMilliseconds.Should().BeLessThan(200); // 200ms threshold
}
```

## Security Test Scenarios

#### SEC-J1: User Access Control
**Test Case**: Validate user can only edit their own entries
```csharp
[Test]
public async Task UpdateJournalEntry_ByDifferentUser_ShouldFail()
{
    // Given: Entry created by user A
    var entry = await CreateTestEntryAsUser("userA", "Original content");
    
    var updateRequest = new UpdateJournalEntryRequest
    {
        Id = entry.Id,
        Content = "Modified content"
    };
    
    // When: User B tries to update
    var context = CreateUserContext("userB");
    
    // Then: Should throw unauthorized exception
    await FluentActions.Invoking(() => updateHandler.Handle(updateRequest, CancellationToken.None))
        .Should().ThrowAsync<UnauthorizedAccessException>();
}
```

#### SEC-J2: Input Sanitization
**Test Case**: Validate HTML content is properly sanitized
```csharp
[Test]
public async Task CreateJournalEntry_WithMaliciousContent_ShouldSanitize()
{
    // Given: Entry with potentially malicious HTML
    var request = new CreateJournalEntryRequest
    {
        Title = "Test Entry",
        Content = "<script>alert('xss')</script><p>Safe content</p>",
        EntryDate = DateTime.Today
    };
    
    // When: Creating entry
    var result = await handler.Handle(request, CancellationToken.None);
    
    // Then: Should sanitize malicious content
    var entry = await GetJournalEntry(result.Id);
    entry.Content.Should().NotContain("<script>");
    entry.Content.Should().Contain("<p>Safe content</p>");
}
```

## Test Data Setup

### Sample Journal Entries
```csharp
private static JournalEntry[] GetSampleEntries() => new[]
{
    new JournalEntry
    {
        Title = "Quality Issue Report",
        Content = "Found texture consistency issue in CREAM-001 batch #456",
        EntryDate = DateTime.Today.AddDays(-2),
        AssociatedProducts = new[] { CreateProductAssociation("CREAM-001") },
        Tags = new[] { CreateTag("quality"), CreateTag("production") }
    },
    new JournalEntry
    {
        Title = "Product Line Review",
        Content = "All cream products performing well this quarter",
        EntryDate = DateTime.Today.AddDays(-7),
        AssociatedProductFamilies = new[] { CreateProductFamilyAssociation("CREAM-") },
        Tags = new[] { CreateTag("review"), CreateTag("performance") }
    },
    new JournalEntry
    {
        Title = "Supplier Meeting Notes",
        Content = "Discussed new packaging options with Supplier X",
        EntryDate = DateTime.Today.AddDays(-14),
        AssociatedProductFamilies = new[] { CreateProductFamilyAssociation("PACK-") },
        Tags = new[] { CreateTag("supplier"), CreateTag("packaging") }
    }
};
```

## Test Environment Setup

### Database Seeding for Tests
```csharp
public static class JournalTestDataSeeder
{
    public static async Task SeedJournalTestData(ApplicationDbContext context)
    {
        // Seed sample products for association testing
        await SeedProducts(context);
        
        // Seed sample journal entries
        var entries = GetSampleEntries();
        await context.JournalEntries.AddRangeAsync(entries);
        
        // Seed tags
        await SeedTags(context);
        
        await context.SaveChangesAsync();
    }
}
```

### API Testing Configuration
```json
{
  "TestSettings": {
    "DatabaseConnection": "TestDatabase",
    "MockAuthentication": true,
    "TestUserId": "test-user-123",
    "SeedTestData": true,
    "JournalTestEntries": 100
  }
}
```

## Test Execution Strategy

### Unit Test Execution
```bash
# Run all journal unit tests
dotnet test --filter "Category=Journal&Category=Unit"

# Run specific test class
dotnet test --filter "ClassName~JournalEntryHandlerTests"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Integration Test Execution
```bash
# Run journal integration tests
dotnet test --filter "Category=Journal&Category=Integration"

# Run with test database
ASPNETCORE_ENVIRONMENT=Test dotnet test --filter "Category=Integration"
```

### UI Test Execution
```bash
# Run all journal UI tests
npx playwright test tests/journal/

# Run specific test file
npx playwright test tests/journal/journal-management.spec.js

# Run with headed browser for debugging
npx playwright test tests/journal/ --headed

# Run with specific browser
npx playwright test tests/journal/ --project=chromium
```

### Test Reporting
- **Unit Tests**: Generate coverage reports and publish to CI/CD
- **Integration Tests**: Generate API test reports with response times
- **UI Tests**: Generate Playwright HTML reports with screenshots
- **Performance Tests**: Generate benchmark reports with timing metrics

This comprehensive test specification ensures the Journal feature is thoroughly validated across all implementation phases and integration points.