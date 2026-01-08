# Remove M3 Margin and Costs Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Complete removal of M3 margin level and its associated costs from both backend and frontend

**Architecture:** The M3 margin level was originally introduced as a separate overhead/regime cost layer. After redesign, M3 was marked as obsolete and mapped to M2. This plan removes all M3 references, leaving only M0, M1, M2 margin levels.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core, React, TypeScript, NSwag (OpenAPI client generation)

---

## Task 1: Remove M3 from Domain Layer - MarginData

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/MarginData.cs`

**Step 1: Read the current MarginData.cs file**

Verify the current implementation with Obsolete M3 property.

Run: Read the file to confirm structure
Expected: File contains Obsolete M3 property mapping to M2

**Step 2: Remove M3 Obsolete property**

Remove the following lines:
```csharp
[Obsolete("Use M2 instead. The old M3 property has been renamed to M2.")]
public MarginLevel M3 => M2;
```

**Step 3: Verify file compiles**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: Compilation errors in dependent projects (expected at this stage)

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/MarginData.cs
git commit -m "feat(domain): remove obsolete M3 property from MarginData

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 2: Remove M3 from Domain Layer - AnalyticsProduct

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs`

**Step 1: Read the current AnalyticsProduct.cs file**

Verify the M3Amount and M3Percentage properties exist.

Run: Read the file
Expected: File contains M3Amount and M3Percentage properties (lines 22-23, 28)

**Step 2: Remove M3 properties**

Remove these lines:
```csharp
public decimal M3Amount { get; init; } // M2 + overhead margin (net profitability)
```
```csharp
public decimal M3Percentage { get; init; }
```

**Step 3: Update XML comments**

Update the comment to reflect only M0-M2:
```csharp
// M0-M2 margin levels - amounts
public decimal M0Amount { get; init; }
public decimal M1Amount { get; init; }
public decimal M2Amount { get; init; }

// M0-M2 margin levels - percentages
public decimal M0Percentage { get; init; }
public decimal M1Percentage { get; init; }
public decimal M2Percentage { get; init; }
```

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs
git commit -m "feat(domain): remove M3Amount and M3Percentage from AnalyticsProduct

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 3: Remove M3 from Application Layer - DTOs (Part 1)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ProductMarginDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MonthlyMarginDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MarginHistoryDto.cs`

**Step 1: Remove M3 from ProductMarginDto**

Remove line 18:
```csharp
public MarginLevelDto M3 { get; set; } = new();  // Net profitability
```

Update comment on line 14-17 to:
```csharp
// Margin levels - structured breakdown (M0-M2)
public MarginLevelDto M0 { get; set; } = new();  // Direct material margin
public MarginLevelDto M1 { get; set; } = new();  // Manufacturing margin
public MarginLevelDto M2 { get; set; } = new();  // Sales & marketing margin
```

**Step 2: Remove M3 from MonthlyMarginDto**

Remove line 9:
```csharp
public MarginLevelDto M3 { get; set; } = new();
```

**Step 3: Remove M3 from MarginHistoryDto**

Remove lines 26-27:
```csharp
[JsonPropertyName("m3")]
public MarginLevelDto M3 { get; set; } = new();
```

Update comment on line 16:
```csharp
// M0-M2 margin levels
```

**Step 4: Verify build (expect errors)**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Compilation errors in handlers and services (will be fixed in next tasks)

**Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ProductMarginDto.cs
git add backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MonthlyMarginDto.cs
git add backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MarginHistoryDto.cs
git commit -m "feat(application): remove M3 from margin DTOs

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 4: Update MarginCalculator to remove M3 support

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Analytics/MarginCalculator.cs`

**Step 1: Read current MarginCalculator implementation**

Check the GetMarginAmountForLevel method (lines 85-95).

Run: Read the file
Expected: Method has "M3" case in switch expression

**Step 2: Update method signature to change default**

Change line 16 from:
```csharp
string marginLevel = "M3",
```

To:
```csharp
string marginLevel = "M2",
```

**Step 3: Remove M3 case from switch expression**

Change lines 85-95 from:
```csharp
public decimal GetMarginAmountForLevel(AnalyticsProduct product, string marginLevel)
{
    return marginLevel.ToUpperInvariant() switch
    {
        "M0" => product.M0Amount,
        "M1" => product.M1Amount,
        "M2" => product.M2Amount,
        "M3" => product.M3Amount,
        _ => product.M3Amount // Default to M3
    };
}
```

To:
```csharp
public decimal GetMarginAmountForLevel(AnalyticsProduct product, string marginLevel)
{
    return marginLevel.ToUpperInvariant() switch
    {
        "M0" => product.M0Amount,
        "M1" => product.M1Amount,
        "M2" => product.M2Amount,
        _ => product.M2Amount // Default to M2
    };
}
```

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Analytics/MarginCalculator.cs
git commit -m "feat(domain): remove M3 support from MarginCalculator, default to M2

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 5: Update AnalyticsRepository to remove M3 mapping

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs`

**Step 1: Read current AnalyticsRepository implementation**

Check lines 89-99 where M3Amount and M3Percentage are mapped.

Run: Read lines 80-120
Expected: M3Amount and M3Percentage mappings exist

**Step 2: Remove M3 property mappings**

Remove lines 93 and 99:
```csharp
M3Amount = latestMarginEntry.Equals(default(KeyValuePair<DateTime, MarginData>)) ? 0 : latestMarginEntry.Value.M3.Amount,
```
```csharp
M3Percentage = latestMarginEntry.Equals(default(KeyValuePair<DateTime, MarginData>)) ? 0 : latestMarginEntry.Value.M3.Percentage,
```

Update comment on line 89:
```csharp
// M0-M2 margin amounts
```

Update comment on line 95:
```csharp
// M0-M2 margin percentages
```

**Step 3: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Should compile without errors (other handlers may still have issues)

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs
git commit -m "feat(analytics): remove M3 mapping from AnalyticsRepository

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 6: Find and update all handler usages of M3

**Files:**
- Modify: Multiple handler files

**Step 1: Search for M3 usages in handlers**

Run: `grep -r "\.M3" backend/src/Anela.Heblo.Application/Features/ --include="*.cs"`
Expected: List of files using M3 property

**Step 2: Read each file and identify changes needed**

For each file found:
- Read the file
- Identify M3 property access
- Plan replacement (usually remove or change to M2)

**Step 3: Create a list of files to update**

Document which files need changes and what specific changes.

**Step 4: Update each handler file individually**

For each handler:
- Remove M3 assignments or usage
- Replace with M2 if semantic equivalent is needed
- Update any comments referencing M3

**Step 5: Verify each change compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Clean build

**Step 6: Commit all handler changes**

```bash
git add backend/src/Anela.Heblo.Application/Features/
git commit -m "feat(handlers): remove M3 references from all handlers

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 7: Update backend tests to remove M3

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`
- Modify: Any other test files with M3 references

**Step 1: Search for M3 in test files**

Run: `grep -r "M3" backend/test/ --include="*.cs"`
Expected: List of test files using M3

**Step 2: Update GetMarginReportHandlerTests.cs**

Remove M3 assertions and test data:
- Remove M3 test setup
- Remove M3 assertions
- Update test names if they reference M3

**Step 3: Update GetProductMarginSummaryHandlerTests.cs**

Similar to Step 2:
- Remove M3 test data
- Remove M3 assertions

**Step 4: Run all backend tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: All tests pass

**Step 5: Commit test changes**

```bash
git add backend/test/
git commit -m "test(backend): remove M3 from all backend tests

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 8: Regenerate OpenAPI client for frontend

**Files:**
- Generated: `frontend/src/api/generated/api-client.ts`

**Step 1: Build backend to trigger NSwag generation**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: api-client.ts is regenerated without M3 references

**Step 2: Verify frontend API client no longer has M3**

Run: `grep "m3" frontend/src/api/generated/api-client.ts`
Expected: No matches (or very few unrelated matches)

**Step 3: Commit regenerated client**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore(frontend): regenerate API client without M3

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 9: Update frontend - MarginsSummary component

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsSummary.tsx`

**Step 1: Read current MarginsSummary.tsx**

Verify M3 usage in lines 32-34, 39-42, 182-217.

Run: Read the file
Expected: M3 calculations and M3 table row exist

**Step 2: Remove M3 calculations**

Remove lines 32-34:
```typescript
const averageM3Percentage = safeMarginHistory.length > 0
  ? safeMarginHistory.reduce((sum, m) => sum + (m.m3?.percentage || 0), 0) / safeMarginHistory.length
  : 0;
```

**Step 3: Update margin display to use M2 instead of M3**

Change lines 39-42 from:
```typescript
const margin = averageM3Percentage;
const marginAmount = safeMarginHistory.length > 0
  ? safeMarginHistory.reduce((sum, m) => sum + (m.m3?.amount || 0), 0) / safeMarginHistory.length
  : 0;
```

To:
```typescript
const margin = averageM2Percentage;
const marginAmount = safeMarginHistory.length > 0
  ? safeMarginHistory.reduce((sum, m) => sum + (m.m2?.amount || 0), 0) / safeMarginHistory.length
  : 0;
```

**Step 4: Remove M3 table row**

Remove lines 181-217 (entire M3 row):
```tsx
{/* M3 Row - Red */}
<tr className="bg-red-50 border-l-4 border-red-400">
  ...
</tr>
```

**Step 5: Verify component compiles**

Run: `npm --prefix frontend run build`
Expected: Build succeeds

**Step 6: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsSummary.tsx
git commit -m "feat(frontend): remove M3 from MarginsSummary component

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 10: Update frontend - MarginsChart component

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx`

**Step 1: Read current MarginsChart.tsx**

Verify M3 data arrays and datasets.

Run: Read the file
Expected: m3PercentageData, m3CostLevelData arrays and M3 datasets exist

**Step 2: Remove M3 data arrays initialization**

Remove from lines 42-49:
```typescript
const m3PercentageData = new Array(12).fill(0);
```
```typescript
const m3CostLevelData = new Array(12).fill(0);
```

**Step 3: Remove M3 maps**

Remove from lines 55-62:
```typescript
const m3PercentageMap = new Map<string, number>();
```
```typescript
const m3CostLevelMap = new Map<string, number>();
```

**Step 4: Remove M3 map population**

Remove from lines 78-87:
```typescript
m3PercentageMap.set(key, record.m3?.percentage || 0);
```
```typescript
m3CostLevelMap.set(key, record.m3?.costLevel || 0);
```

**Step 5: Remove M3 array filling**

Remove from lines 104-111:
```typescript
m3PercentageData[i] = m3PercentageMap.get(key) || 0;
```
```typescript
m3CostLevelData[i] = m3CostLevelMap.get(key) || 0;
```

**Step 6: Remove M3 from return object**

Remove from lines 114-122:
```typescript
m3PercentageData,
```
```typescript
m3CostLevelData
```

Update destructuring on lines 126-135.

**Step 7: Update hasM0M3Data check**

Change line 138-141 from:
```typescript
const hasM0M3Data = m0PercentageData.some(value => value > 0) ||
                    m1PercentageData.some(value => value > 0) ||
                    m2PercentageData.some(value => value > 0) ||
                    m3PercentageData.some(value => value > 0);
```

To:
```typescript
const hasMarginData = m0PercentageData.some(value => value > 0) ||
                      m1PercentageData.some(value => value > 0) ||
                      m2PercentageData.some(value => value > 0);
```

**Step 8: Remove M3 styling**

Remove line 147:
```typescript
const m3Styling = generatePointStyling(12, journalEntries, "rgba(239, 68, 68, 1)"); // Red
```

**Step 9: Remove M3 cost level dataset**

Remove from costLevelDatasets array (lines 181-190):
```typescript
{
  type: 'bar' as const,
  label: "M3 - Režijní náklady (Kč/ks)",
  data: m3CostLevelData,
  backgroundColor: "rgba(239, 68, 68, 0.7)", // Red
  borderColor: "rgba(239, 68, 68, 1)",
  borderWidth: 1,
  yAxisID: "y",
  stack: 'costs',
},
```

**Step 10: Remove M3 percentage dataset**

Remove from percentageDatasets array (lines 240-254):
```typescript
{
  type: 'line' as const,
  label: "M3 - Finální marže (%)",
  data: m3PercentageData,
  backgroundColor: "rgba(239, 68, 68, 0.1)",
  borderColor: "rgba(239, 68, 68, 1)",
  borderWidth: 2,
  tension: 0.1,
  pointBackgroundColor: m3Styling.pointBackgroundColors,
  pointBorderColor: m3Styling.pointBackgroundColors,
  pointRadius: m3Styling.pointRadiuses,
  pointHoverRadius: m3Styling.pointHoverRadiuses,
  yAxisID: "y1",
  fill: false,
},
```

**Step 11: Update variable references**

Change line 318:
```typescript
const hasData = hasMarginData;
```

**Step 12: Update conditional checks**

Replace all `hasM0M3Data` with `hasMarginData` throughout the file.

**Step 13: Verify component compiles**

Run: `npm --prefix frontend run build`
Expected: Build succeeds

**Step 14: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx
git commit -m "feat(frontend): remove M3 from MarginsChart component

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 11: Search and update other frontend components with M3

**Files:**
- Modify: `frontend/src/components/pages/ProductMarginsList.tsx` (if exists)
- Modify: `frontend/src/components/pages/ProductMarginSummary.tsx` (if exists)
- Modify: Any other files with M3 references

**Step 1: Search for M3 in frontend**

Run: `grep -r "m3" frontend/src/components --include="*.tsx" --include="*.ts"`
Expected: List of files with m3 references

**Step 2: For each file found, read and analyze**

Identify what needs to be changed:
- Remove M3 columns from tables
- Remove M3 from calculations
- Update default margin level from M3 to M2

**Step 3: Update ProductMarginsList.tsx**

If file exists:
- Remove M3 column from table
- Update any M3 references to M2

**Step 4: Update ProductMarginSummary.tsx**

If file exists:
- Remove M3 display
- Update calculations

**Step 5: Verify all changes compile**

Run: `npm --prefix frontend run build`
Expected: Clean build

**Step 6: Commit**

```bash
git add frontend/src/components/pages/
git commit -m "feat(frontend): remove M3 from page components

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 12: Update frontend tests

**Files:**
- Modify: `frontend/src/components/pages/__tests__/ProductMarginsList.test.tsx`
- Modify: `frontend/src/components/catalog/detail/tabs/MarginsTab/__tests__/MarginsSummary.test.tsx`
- Modify: Any other test files with M3 references

**Step 1: Search for M3 in frontend tests**

Run: `grep -r "m3\|M3" frontend/src --include="*.test.tsx" --include="*.test.ts"`
Expected: List of test files with M3

**Step 2: Update MarginsSummary.test.tsx**

Remove M3 test assertions:
- Remove M3 row expectations
- Update test data to exclude M3

**Step 3: Update ProductMarginsList.test.tsx**

If file exists:
- Remove M3 column expectations
- Update mock data

**Step 4: Run frontend tests**

Run: `npm --prefix frontend test -- --watchAll=false --passWithNoTests`
Expected: All tests pass

**Step 5: Commit**

```bash
git add frontend/src/**/__tests__/
git commit -m "test(frontend): remove M3 from frontend tests

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 13: Update documentation

**Files:**
- Modify: `docs/features/margin-calculation-system.md`
- Modify: `docs/features/margins_v2/spec.md` (if exists)

**Step 1: Read margin-calculation-system.md**

Check for M3 references.

Run: Read the file
Expected: File contains M3 examples and explanations

**Step 2: Update margin level descriptions**

Change all references from "M0-M3" to "M0-M2".

Remove sections explaining M3 as regime/overhead costs.

Update examples to only show M0, M1, M2.

**Step 3: Update code examples in documentation**

Remove M3 from all TypeScript/JavaScript examples.

Example on lines 194-197 should become:
```typescript
const costBreakdown = [
  { name: 'Materiál', value: marginData.M0.CostLevel },
  { name: 'Výroba', value: marginData.M1.CostLevel },
  { name: 'Prodej', value: marginData.M2.CostLevel }
];
```

Remove line referencing M3.CostTotal and M3.CostLevel.

**Step 4: Check margins_v2/spec.md if it exists**

If file exists, update similarly.

**Step 5: Commit**

```bash
git add docs/features/
git commit -m "docs: remove M3 from margin calculation documentation

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 14: Verify complete build and all tests

**Files:**
- None (verification only)

**Step 1: Clean and build backend**

Run: `dotnet clean backend/Anela.Heblo.sln && dotnet build backend/Anela.Heblo.sln`
Expected: Clean build with no errors

**Step 2: Run all backend tests**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: All tests pass

**Step 3: Run dotnet format**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: No formatting issues

**Step 4: Clean and build frontend**

Run: `npm --prefix frontend run build`
Expected: Clean build with no errors

**Step 5: Run frontend linter**

Run: `npm --prefix frontend run lint`
Expected: No linting errors

**Step 6: Run all frontend tests**

Run: `npm --prefix frontend test -- --watchAll=false --passWithNoTests`
Expected: All tests pass

**Step 7: Document verification**

Create verification summary:
- Backend builds: ✓
- Backend tests pass: ✓
- Frontend builds: ✓
- Frontend tests pass: ✓
- Linting passes: ✓

---

## Task 15: Final verification with search

**Files:**
- None (verification only)

**Step 1: Search for any remaining M3 references in backend**

Run: `grep -r "M3" backend/src --include="*.cs" | grep -v "// M0-M3" | grep -v "M0-M3"`
Expected: No matches (or only in comments describing the removal)

**Step 2: Search for any remaining m3 references in frontend code**

Run: `grep -r "m3\|M3" frontend/src --include="*.tsx" --include="*.ts" --exclude-dir=generated | grep -v "//.*m3" | grep -v "M0-M2"`
Expected: No matches in application code (generated API client should also be clean)

**Step 3: Search in tests**

Run: `grep -r "M3" backend/test --include="*.cs"`
Run: `grep -r "m3" frontend/src --include="*.test.tsx"`
Expected: No M3 references in tests

**Step 4: Create final verification report**

Document all searches performed and their results.

---

## Task 16: Update CLAUDE.md if needed

**Files:**
- Modify: `CLAUDE.md` (if it references M3)

**Step 1: Search CLAUDE.md for M3 references**

Run: `grep -n "M3" CLAUDE.md`
Expected: May or may not have M3 references

**Step 2: Update any M3 references**

If found, change to reference only M0-M2 margin levels.

Update any architectural descriptions that mention M3.

**Step 3: Commit if changes made**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md to reflect M0-M2 margin levels

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Summary Checklist

After completing all tasks, verify:

- [ ] Backend Domain layer: MarginData.cs and AnalyticsProduct.cs updated
- [ ] Backend Application layer: All DTOs updated (ProductMarginDto, MonthlyMarginDto, MarginHistoryDto)
- [ ] Backend Application layer: MarginCalculator updated
- [ ] Backend Application layer: AnalyticsRepository updated
- [ ] Backend Application layer: All handlers updated
- [ ] Backend tests: All tests updated and passing
- [ ] OpenAPI client: Regenerated without M3
- [ ] Frontend components: MarginsSummary updated
- [ ] Frontend components: MarginsChart updated
- [ ] Frontend components: All page components updated
- [ ] Frontend tests: All tests updated and passing
- [ ] Documentation: margin-calculation-system.md updated
- [ ] Full backend build passes
- [ ] Full frontend build passes
- [ ] All tests pass (backend + frontend)
- [ ] Linting passes (backend + frontend)
- [ ] No M3 references remain in codebase (verified by search)

## Notes

- This is a breaking change for any external consumers of the API
- The OpenAPI schema version should be bumped if following semantic versioning
- Consider adding a migration guide if this is a public API
- M2 now represents the final margin level (previously M3's role)
- All cost calculations remain the same, only the naming/labeling changes
