# Product Code Mapping Transformation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement `TransformAsync` method to replace product codes during invoice import (e.g., "1287" → "SLU000001")

**Architecture:** Simple foreach loop iterating through invoice items, replacing product codes that exactly match the original code with the new code. Modifies the original object in-place.

**Tech Stack:** .NET 8, xUnit, Moq

---

## Task 1: Create Test File and Setup

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs`

**Step 1: Create test directory structure**

Run:
```bash
mkdir -p backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations
```

Expected: Directory created successfully

**Step 2: Create test file with basic structure**

Create file with:

```csharp
using Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;
using Anela.Heblo.Domain.Features.Invoices;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure.Transformations;

public class ProductMappingIssuedInvoiceImportTransformationTests
{
    private const string OriginalCode = "TEST001";
    private const string NewCode = "NEW001";

    private readonly ProductMappingIssuedInvoiceImportTransformation _transformation;

    public ProductMappingIssuedInvoiceImportTransformationTests()
    {
        _transformation = new ProductMappingIssuedInvoiceImportTransformation(OriginalCode, NewCode);
    }
}
```

**Step 3: Verify file compiles**

Run:
```bash
cd backend/test/Anela.Heblo.Tests
dotnet build
```

Expected: Build succeeds

**Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs
git commit -m "test: add test file structure for ProductMappingIssuedInvoiceImportTransformation"
```

---

## Task 2: Single Item Replacement (TDD)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformation.cs:17-23`

**Step 1: Write failing test**

Add to test class:

```csharp
[Fact]
public async Task TransformAsync_WithSingleMatchingItem_ReplacesProductCode()
{
    // Arrange
    var invoiceDetail = new IssuedInvoiceDetail
    {
        Items = new List<IssuedInvoiceDetailItem>
        {
            new IssuedInvoiceDetailItem { Code = OriginalCode, Name = "Test Product" }
        }
    };

    // Act
    var result = await _transformation.TransformAsync(invoiceDetail);

    // Assert
    Assert.Single(result.Items);
    Assert.Equal(NewCode, result.Items[0].Code);
    Assert.Equal("Test Product", result.Items[0].Name); // Other properties unchanged
}
```

**Step 2: Run test to verify it fails**

Run:
```bash
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~ProductMappingIssuedInvoiceImportTransformationTests.TransformAsync_WithSingleMatchingItem_ReplacesProductCode"
```

Expected: FAIL - product code is still "TEST001" instead of "NEW001"

**Step 3: Implement TransformAsync method**

In `ProductMappingIssuedInvoiceImportTransformation.cs`, replace lines 17-23:

```csharp
public Task<IssuedInvoiceDetail> TransformAsync(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default)
{
    foreach (var item in invoiceDetail.Items)
    {
        if (item.Code == _originalProductCode)
        {
            item.Code = _newProductCode;
        }
    }

    return Task.FromResult(invoiceDetail);
}
```

**Step 4: Run test to verify it passes**

Run:
```bash
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~ProductMappingIssuedInvoiceImportTransformationTests.TransformAsync_WithSingleMatchingItem_ReplacesProductCode"
```

Expected: PASS

**Step 5: Run dotnet format**

Run:
```bash
cd backend
dotnet format
```

Expected: No formatting changes needed

**Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformation.cs
git add backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs
git commit -m "feat: implement product code replacement in TransformAsync

Replaces product codes in invoice items when exact match is found.
Modifies original object in-place."
```

---

## Task 3: Multiple Items Replacement (TDD)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs`

**Step 1: Write test for multiple matching items**

Add to test class:

```csharp
[Fact]
public async Task TransformAsync_WithMultipleMatchingItems_ReplacesAllOccurrences()
{
    // Arrange
    var invoiceDetail = new IssuedInvoiceDetail
    {
        Items = new List<IssuedInvoiceDetailItem>
        {
            new IssuedInvoiceDetailItem { Code = OriginalCode, Name = "Product A" },
            new IssuedInvoiceDetailItem { Code = OriginalCode, Name = "Product B" },
            new IssuedInvoiceDetailItem { Code = OriginalCode, Name = "Product C" }
        }
    };

    // Act
    var result = await _transformation.TransformAsync(invoiceDetail);

    // Assert
    Assert.Equal(3, result.Items.Count);
    Assert.All(result.Items, item => Assert.Equal(NewCode, item.Code));
    Assert.Equal("Product A", result.Items[0].Name);
    Assert.Equal("Product B", result.Items[1].Name);
    Assert.Equal("Product C", result.Items[2].Name);
}
```

**Step 2: Run test to verify it passes**

Run:
```bash
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~ProductMappingIssuedInvoiceImportTransformationTests.TransformAsync_WithMultipleMatchingItems_ReplacesAllOccurrences"
```

Expected: PASS (implementation already handles this)

**Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs
git commit -m "test: verify multiple items replacement works correctly"
```

---

## Task 4: No Matches Case (TDD)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs`

**Step 1: Write test for no matching items**

Add to test class:

```csharp
[Fact]
public async Task TransformAsync_WithNoMatchingItems_LeavesInvoiceUnchanged()
{
    // Arrange
    var invoiceDetail = new IssuedInvoiceDetail
    {
        Items = new List<IssuedInvoiceDetailItem>
        {
            new IssuedInvoiceDetailItem { Code = "OTHER001", Name = "Product A" },
            new IssuedInvoiceDetailItem { Code = "OTHER002", Name = "Product B" }
        }
    };

    // Act
    var result = await _transformation.TransformAsync(invoiceDetail);

    // Assert
    Assert.Equal(2, result.Items.Count);
    Assert.Equal("OTHER001", result.Items[0].Code);
    Assert.Equal("OTHER002", result.Items[1].Code);
    Assert.Equal("Product A", result.Items[0].Name);
    Assert.Equal("Product B", result.Items[1].Name);
}
```

**Step 2: Run test to verify it passes**

Run:
```bash
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~ProductMappingIssuedInvoiceImportTransformationTests.TransformAsync_WithNoMatchingItems_LeavesInvoiceUnchanged"
```

Expected: PASS (implementation already handles this)

**Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs
git commit -m "test: verify no matches case leaves invoice unchanged"
```

---

## Task 5: Empty Items Case (TDD)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs`

**Step 1: Write test for empty items list**

Add to test class:

```csharp
[Fact]
public async Task TransformAsync_WithEmptyItemsList_ReturnsInvoiceUnchanged()
{
    // Arrange
    var invoiceDetail = new IssuedInvoiceDetail
    {
        Items = new List<IssuedInvoiceDetailItem>()
    };

    // Act
    var result = await _transformation.TransformAsync(invoiceDetail);

    // Assert
    Assert.Empty(result.Items);
}
```

**Step 2: Run test to verify it passes**

Run:
```bash
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~ProductMappingIssuedInvoiceImportTransformationTests.TransformAsync_WithEmptyItemsList_ReturnsInvoiceUnchanged"
```

Expected: PASS (foreach handles empty collections)

**Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs
git commit -m "test: verify empty items list is handled correctly"
```

---

## Task 6: Mixed Items Case (TDD)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs`

**Step 1: Write test for mixed matching and non-matching items**

Add to test class:

```csharp
[Fact]
public async Task TransformAsync_WithMixedItems_ReplacesOnlyMatchingItems()
{
    // Arrange
    var invoiceDetail = new IssuedInvoiceDetail
    {
        Items = new List<IssuedInvoiceDetailItem>
        {
            new IssuedInvoiceDetailItem { Code = "OTHER001", Name = "Product A" },
            new IssuedInvoiceDetailItem { Code = OriginalCode, Name = "Product B" },
            new IssuedInvoiceDetailItem { Code = "OTHER002", Name = "Product C" },
            new IssuedInvoiceDetailItem { Code = OriginalCode, Name = "Product D" }
        }
    };

    // Act
    var result = await _transformation.TransformAsync(invoiceDetail);

    // Assert
    Assert.Equal(4, result.Items.Count);
    Assert.Equal("OTHER001", result.Items[0].Code);
    Assert.Equal(NewCode, result.Items[1].Code);
    Assert.Equal("OTHER002", result.Items[2].Code);
    Assert.Equal(NewCode, result.Items[3].Code);
}
```

**Step 2: Run test to verify it passes**

Run:
```bash
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~ProductMappingIssuedInvoiceImportTransformationTests.TransformAsync_WithMixedItems_ReplacesOnlyMatchingItems"
```

Expected: PASS (implementation already handles this)

**Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs
git commit -m "test: verify mixed items with selective replacement"
```

---

## Task 7: Case Sensitivity Test (TDD)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs`

**Step 1: Write test for case-sensitive matching**

Add to test class:

```csharp
[Fact]
public async Task TransformAsync_IsCaseSensitive_DoesNotReplaceWrongCase()
{
    // Arrange
    var invoiceDetail = new IssuedInvoiceDetail
    {
        Items = new List<IssuedInvoiceDetailItem>
        {
            new IssuedInvoiceDetailItem { Code = OriginalCode.ToLower(), Name = "Product A" },
            new IssuedInvoiceDetailItem { Code = OriginalCode, Name = "Product B" }
        }
    };

    // Act
    var result = await _transformation.TransformAsync(invoiceDetail);

    // Assert
    Assert.Equal(2, result.Items.Count);
    Assert.Equal(OriginalCode.ToLower(), result.Items[0].Code); // Not replaced (wrong case)
    Assert.Equal(NewCode, result.Items[1].Code); // Replaced (exact match)
}
```

**Step 2: Run test to verify it passes**

Run:
```bash
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~ProductMappingIssuedInvoiceImportTransformationTests.TransformAsync_IsCaseSensitive_DoesNotReplaceWrongCase"
```

Expected: PASS (default string equality is case-sensitive)

**Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs
git commit -m "test: verify case-sensitive product code matching"
```

---

## Task 8: Run All Tests and Final Verification

**Step 1: Run all transformation tests**

Run:
```bash
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~ProductMappingIssuedInvoiceImportTransformationTests"
```

Expected: All 6 tests PASS

**Step 2: Run full backend test suite**

Run:
```bash
cd backend
dotnet test
```

Expected: All tests PASS

**Step 3: Verify formatting**

Run:
```bash
cd backend
dotnet format --verify-no-changes
```

Expected: No formatting issues

**Step 4: Build solution**

Run:
```bash
cd backend
dotnet build
```

Expected: Build succeeds with no warnings

---

## Summary

**Implementation complete when:**
- ✅ All 6 unit tests passing
- ✅ Code formatted with `dotnet format`
- ✅ Full backend test suite passes
- ✅ Solution builds successfully

**Files modified:**
1. `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformation.cs` - Implementation
2. `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs` - Tests

**Test coverage:**
- Single item replacement ✅
- Multiple items replacement ✅
- No matches ✅
- Empty items list ✅
- Mixed items ✅
- Case sensitivity ✅
