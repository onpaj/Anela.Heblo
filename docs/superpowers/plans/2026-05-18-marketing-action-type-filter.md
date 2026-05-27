# Marketing Action Type Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire up the existing-but-dead `ActionType` filter on the marketing actions list endpoint and surface it as a dropdown in the list page filter bar.

**Architecture:** Pure additive change inside the Marketing vertical slice. Add `MarketingActionType? ActionType` to the request DTO, copy it onto `MarketingActionQueryCriteria` in the handler (the repository branch and DB index already exist), regenerate the TS client, then add a dropdown to `MarketingActionFilters` backed by a new shared label-map module. The map module also replaces the broken `ACTION_TYPE_LABELS`/`ACTION_TYPE_BADGE` constants in `MarketingActionGrid` (their keys today do not match the real enum and the labels are mis-assigned).

**Tech Stack:** .NET 8, ASP.NET Core MVC, MediatR, EF Core (existing); React + TypeScript, React Query, NSwag-generated client, Jest + React Testing Library (existing).

---

## File Structure

**New files**
- `frontend/src/components/marketing/list/marketingActionTypeLabels.ts` — canonical `MarketingActionType`-keyed label, badge, and ordered-options dictionaries.
- `frontend/src/components/marketing/list/__tests__/MarketingActionFilters.test.tsx` — component test covering dropdown rendering, change-callback, clear, and reset behaviour.
- `backend/test/Anela.Heblo.Tests/Application/Marketing/GetMarketingActionsHandlerTests.cs` — handler unit tests for `ActionType` mapping (positive + negative).
- `backend/test/Anela.Heblo.Tests/Controllers/MarketingCalendarControllerTests.cs` — minimal integration test using `HebloWebApplicationFactory` covering the `?ActionType=` happy path and a `400` for an invalid enum value.

**Modified files**
- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/GetMarketingActionsRequest.cs` — add `MarketingActionType? ActionType` property + `using` directive.
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingActions/GetMarketingActionsHandler.cs` — map `request.ActionType` onto the criteria.
- `frontend/src/components/marketing/list/MarketingActionFilters.tsx` — extend `Filters`, `EMPTY_FILTERS`, `hasActiveFilters`, render the new dropdown as the first control.
- `frontend/src/components/marketing/list/MarketingActionGrid.tsx` — replace bogus local maps with imports from the new shared module.
- `frontend/src/components/marketing/list/__tests__/MarketingActionGrid.test.tsx` — replace fixtures and assertions using `Campaign`/`Launch`/`General` with real enum values.
- `frontend/src/api/hooks/useMarketingCalendar.ts` — add `actionType?: MarketingActionType` to `GetMarketingActionsParams` and pass it positionally to the regenerated client method.
- `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` — forward `filters.actionType` (or `undefined`) to `useMarketingActions`.
- `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx` — sync the mocked `EMPTY_FILTERS` literal with the new shape.

---

## Pre-task setup

Confirm the worktree is clean and the working directory is the worktree root:

```bash
git status
pwd
# Expected: /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-marketing-actiontype-filter-
```

All commands below assume this CWD.

---

## Task 1: Backend — failing handler test for `ActionType` mapping

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Application/Marketing/GetMarketingActionsHandlerTests.cs`

- [ ] **Step 1: Create the failing handler unit test**

Create file `backend/test/Anela.Heblo.Tests/Application/Marketing/GetMarketingActionsHandlerTests.cs` with the following content:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.UseCases.GetMarketingActions;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Xcc.Persistance;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.Marketing;

public class GetMarketingActionsHandlerTests
{
    private readonly Mock<IMarketingActionRepository> _repository = new();
    private readonly GetMarketingActionsHandler _handler;

    public GetMarketingActionsHandlerTests()
    {
        _repository
            .Setup(x => x.GetPagedAsync(It.IsAny<MarketingActionQueryCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<MarketingAction>
            {
                Items = new List<MarketingAction>(),
                TotalCount = 0,
                PageNumber = 1,
                PageSize = 20,
            });

        _handler = new GetMarketingActionsHandler(_repository.Object);
    }

    [Fact]
    public async Task Handle_WhenActionTypeProvided_PropagatesItToCriteria()
    {
        // Arrange
        var request = new GetMarketingActionsRequest { ActionType = MarketingActionType.Blog };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repository.Verify(
            r => r.GetPagedAsync(
                It.Is<MarketingActionQueryCriteria>(c => c.ActionType == MarketingActionType.Blog),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenActionTypeOmitted_PassesNullActionTypeOnCriteria()
    {
        // Arrange
        var request = new GetMarketingActionsRequest();

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repository.Verify(
            r => r.GetPagedAsync(
                It.Is<MarketingActionQueryCriteria>(c => c.ActionType == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

- [ ] **Step 2: Confirm the test fails to compile**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: build error — `GetMarketingActionsRequest` has no property `ActionType`.

- [ ] **Step 3: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Application/Marketing/GetMarketingActionsHandlerTests.cs
git commit -m "test: add failing GetMarketingActionsHandler ActionType propagation test"
```

---

## Task 2: Backend — add `ActionType` to the request DTO

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/GetMarketingActionsRequest.cs`

- [ ] **Step 1: Add the enum import**

Edit `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/GetMarketingActionsRequest.cs`. Add the following `using` directive in alphabetical order with the others:

```csharp
using Anela.Heblo.Domain.Features.Marketing;
```

- [ ] **Step 2: Add the `ActionType` property**

In the same file, insert the new property immediately after `SearchTerm` so the regenerated TS client lists it as the fourth positional argument. The class should look like this (only the new line is added):

```csharp
public class GetMarketingActionsRequest : IRequest<GetMarketingActionsResponse>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SearchTerm { get; set; }
    public MarketingActionType? ActionType { get; set; }
    public string? ProductCodePrefix { get; set; }
    public DateTime? StartDateFrom { get; set; }
    public DateTime? StartDateTo { get; set; }
    public DateTime? EndDateFrom { get; set; }
    public DateTime? EndDateTo { get; set; }
    public bool IncludeDeleted { get; set; } = false;
}
```

- [ ] **Step 3: Re-run the handler test — it still fails on assertion**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GetMarketingActionsHandlerTests`
Expected: `Handle_WhenActionTypeProvided_PropagatesItToCriteria` FAILS — handler still does not map `ActionType` so the verified criteria has `ActionType == null`. The negative test passes already.

---

## Task 3: Backend — map `ActionType` in the handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingActions/GetMarketingActionsHandler.cs:24-35`

- [ ] **Step 1: Add the mapping**

Edit the `criteria` initializer in `Handle(...)`. The full block (existing lines 24–35) must now read:

```csharp
var criteria = new MarketingActionQueryCriteria
{
    PageNumber = request.PageNumber,
    PageSize = request.PageSize,
    SearchTerm = request.SearchTerm,
    ActionType = request.ActionType,
    ProductCodePrefix = request.ProductCodePrefix,
    StartDateFrom = request.StartDateFrom,
    StartDateTo = request.StartDateTo,
    EndDateFrom = request.EndDateFrom,
    EndDateTo = request.EndDateTo,
    IncludeDeleted = request.IncludeDeleted,
};
```

- [ ] **Step 2: Re-run the handler test — both pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GetMarketingActionsHandlerTests`
Expected: 2 passed.

- [ ] **Step 3: Format and full backend build**

Run: `dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests && dotnet build backend/Anela.Heblo.sln`
Expected: 0 errors.

- [ ] **Step 4: Commit the BE wiring**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/GetMarketingActionsRequest.cs \
  backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingActions/GetMarketingActionsHandler.cs
git commit -m "feat: wire ActionType filter in GetMarketingActions request and handler"
```

---

## Task 4: Backend — controller-level binding test (happy path + 400)

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Controllers/MarketingCalendarControllerTests.cs`

- [ ] **Step 1: Write the failing integration test**

Create `backend/test/Anela.Heblo.Tests/Controllers/MarketingCalendarControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Persistence;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class MarketingCalendarControllerTests : IClassFixture<HebloWebApplicationFactory>
{
    private readonly HebloWebApplicationFactory _factory;

    public MarketingCalendarControllerTests(HebloWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetMarketingActions_WithActionTypeQuery_ReturnsOnlyMatchingActions()
    {
        // Arrange
        await _factory.ClearDatabaseAsync();
        await _factory.SeedDatabaseAsync(async ctx =>
        {
            ctx.Set<MarketingAction>().AddRange(
                new MarketingAction
                {
                    Title = "Blog post",
                    ActionType = MarketingActionType.Blog,
                    StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                },
                new MarketingAction
                {
                    Title = "Newsletter",
                    ActionType = MarketingActionType.Newsletter,
                    StartDate = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc),
                });
            await Task.CompletedTask;
        });

        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/MarketingCalendar?ActionType=Blog");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetMarketingActionsResponse>();
        body.Should().NotBeNull();
        body!.Actions.Should().HaveCount(1);
        body.Actions[0].Title.Should().Be("Blog post");
        body.Actions[0].ActionType.Should().Be("Blog");
    }

    [Fact]
    public async Task GetMarketingActions_WithInvalidActionType_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/MarketingCalendar?ActionType=NotAType");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Run the integration tests — should pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~MarketingCalendarControllerTests`
Expected: 2 passed (BE wiring from Task 2/3 already supports `?ActionType=Blog`; ASP.NET model binding handles the 400 for an invalid enum value).

If a test fails because seed data is shared across runs of `MarketingCalendarController` (e.g. duplicate rows from a previous test), confirm `ClearDatabaseAsync()` ran before the seed — that is the responsibility of the happy-path test only.

- [ ] **Step 3: Commit the integration tests**

```bash
git add backend/test/Anela.Heblo.Tests/Controllers/MarketingCalendarControllerTests.cs
git commit -m "test: cover MarketingCalendar GetMarketingActions ActionType query binding"
```

---

## Task 5: Regenerate the OpenAPI TypeScript client

The frontend client method `marketingCalendar_GetMarketingActions` is generated from the backend OpenAPI document. After Task 3 the spec changed, so we must regenerate before touching frontend code that passes the new arg.

- [ ] **Step 1: Build frontend (triggers OpenAPI client regeneration)**

Run: `npm --prefix frontend run build`
Expected: build succeeds. The generated file `frontend/src/api/generated/api-client.ts` is updated in-place. Note: the production CI step that performs the regeneration runs as part of `npm run build`. If your local environment short-circuits regeneration, run `cd frontend && npm run generate-api && npm run build` instead.

- [ ] **Step 2: Verify the regenerated signature**

Run: `git diff frontend/src/api/generated/api-client.ts | head -80`
Expected: the diff includes a new `actionType: MarketingActionType | null | undefined` parameter in `marketingCalendar_GetMarketingActions`, slotted **after `searchTerm` and before `productCodePrefix`**, and a new `if (actionType !== undefined && actionType !== null) url_ += "ActionType=..."` block. If the ordering differs, adjust the parameter order in Task 7's hook call to match exactly what the regenerated method exposes (the regenerated method is the source of truth).

- [ ] **Step 3: Commit the regenerated client**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate TS client with ActionType query parameter"
```

---

## Task 6: Frontend — shared `marketingActionTypeLabels` module

**Files:**
- Create: `frontend/src/components/marketing/list/marketingActionTypeLabels.ts`

- [ ] **Step 1: Create the canonical label/badge/options module**

Write `frontend/src/components/marketing/list/marketingActionTypeLabels.ts`:

```ts
import { MarketingActionType } from '../../../api/generated/api-client';

export const ACTION_TYPE_LABELS: Record<MarketingActionType, string> = {
  [MarketingActionType.SocialMedia]: 'Sociální sítě',
  [MarketingActionType.Blog]: 'Blog',
  [MarketingActionType.Newsletter]: 'Newsletter',
  [MarketingActionType.PR]: 'PR',
  [MarketingActionType.Event]: 'Událost',
  [MarketingActionType.Meeting]: 'Meeting',
};

export const ACTION_TYPE_BADGE: Record<MarketingActionType, string> = {
  [MarketingActionType.SocialMedia]: 'bg-yellow-100 text-yellow-800',
  [MarketingActionType.Blog]: 'bg-green-100 text-green-800',
  [MarketingActionType.Newsletter]: 'bg-purple-100 text-purple-800',
  [MarketingActionType.PR]: 'bg-orange-100 text-orange-800',
  [MarketingActionType.Event]: 'bg-red-100 text-red-800',
  [MarketingActionType.Meeting]: 'bg-teal-100 text-teal-800',
};

export const ALL_ACTION_TYPE_OPTIONS: ReadonlyArray<MarketingActionType> = [
  MarketingActionType.SocialMedia,
  MarketingActionType.Blog,
  MarketingActionType.Newsletter,
  MarketingActionType.PR,
  MarketingActionType.Event,
  MarketingActionType.Meeting,
];
```

- [ ] **Step 2: Type-check the new module**

Run: `npm --prefix frontend run build`
Expected: build succeeds. (No consumers yet — this only verifies the module compiles.)

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/marketing/list/marketingActionTypeLabels.ts
git commit -m "feat: add canonical MarketingActionType label/badge/options module"
```

---

## Task 7: Frontend — extend `useMarketingActions` hook

**Files:**
- Modify: `frontend/src/api/hooks/useMarketingCalendar.ts`

- [ ] **Step 1: Extend `GetMarketingActionsParams` and pass the new arg**

Edit `frontend/src/api/hooks/useMarketingCalendar.ts`. Two precise changes:

(a) Add a `MarketingActionType` import at the top, immediately after the existing imports:

```ts
import { MarketingActionType } from "../generated/api-client";
```

(b) Replace the existing `GetMarketingActionsParams` interface (lines 4–14) **and** the existing `useMarketingActions` query function (lines 42–62) with the version below. The new `actionType` argument is inserted **after `searchTerm`** in the positional call, matching the regenerated client signature verified in Task 5 Step 2.

```ts
interface GetMarketingActionsParams {
  pageNumber?: number;
  pageSize?: number;
  searchTerm?: string;
  actionType?: MarketingActionType;
  productCodePrefix?: string;
  startDateFrom?: Date;
  startDateTo?: Date;
  endDateFrom?: Date;
  endDateTo?: Date;
  includeDeleted?: boolean;
}
```

```ts
export const useMarketingActions = (
  params: GetMarketingActionsParams = {},
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.marketingCalendar, "actions", params],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      return await (client as any).marketingCalendar_GetMarketingActions(
        params.pageNumber,
        params.pageSize,
        params.searchTerm,
        params.actionType,
        params.productCodePrefix,
        params.startDateFrom,
        params.startDateTo,
        params.endDateFrom,
        params.endDateTo,
        params.includeDeleted,
      );
    },
  });
};
```

- [ ] **Step 2: Type-check**

Run: `npm --prefix frontend run build`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/useMarketingCalendar.ts
git commit -m "feat: forward actionType param through useMarketingActions"
```

---

## Task 8: Frontend — failing `MarketingActionFilters` component test

**Files:**
- Test: `frontend/src/components/marketing/list/__tests__/MarketingActionFilters.test.tsx`

- [ ] **Step 1: Create the failing component test**

Create `frontend/src/components/marketing/list/__tests__/MarketingActionFilters.test.tsx`:

```tsx
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import MarketingActionFilters, { EMPTY_FILTERS, type MarketingFilters } from "../MarketingActionFilters";
import { MarketingActionType } from "../../../../api/generated/api-client";

const noop = () => {};

function renderFilters(overrides: Partial<{
  filters: MarketingFilters;
  onChange: (f: MarketingFilters) => void;
  onClear: () => void;
}> = {}) {
  const props = {
    filters: overrides.filters ?? EMPTY_FILTERS,
    onChange: overrides.onChange ?? noop,
    onClear: overrides.onClear ?? noop,
  };
  return render(<MarketingActionFilters {...props} />);
}

describe("MarketingActionFilters — Typ akce dropdown", () => {
  it("renders the dropdown with 'Všechny typy' as the default option label", () => {
    renderFilters();
    const select = screen.getByLabelText("Typ akce") as HTMLSelectElement;
    expect(select).toBeInTheDocument();
    expect(select.value).toBe("");
    expect(screen.getByRole("option", { name: "Všechny typy" })).toBeInTheDocument();
  });

  it("renders all six action-type options with Czech labels", () => {
    renderFilters();
    expect(screen.getByRole("option", { name: "Sociální sítě" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Blog" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Newsletter" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "PR" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Událost" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Meeting" })).toBeInTheDocument();
  });

  it("renders Typ akce as the first control, left of the search input", () => {
    renderFilters();
    const select = screen.getByLabelText("Typ akce");
    const search = screen.getByPlaceholderText("Hledat název...");
    expect(select.compareDocumentPosition(search) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("calls onChange with actionType set when an option is selected", () => {
    const onChange = jest.fn();
    renderFilters({ onChange });
    const select = screen.getByLabelText("Typ akce");
    fireEvent.change(select, { target: { value: MarketingActionType.Blog } });
    expect(onChange).toHaveBeenCalledWith({
      ...EMPTY_FILTERS,
      actionType: MarketingActionType.Blog,
    });
  });

  it("calls onChange with actionType cleared when 'Všechny typy' is re-selected", () => {
    const onChange = jest.fn();
    renderFilters({
      filters: { ...EMPTY_FILTERS, actionType: MarketingActionType.Blog },
      onChange,
    });
    const select = screen.getByLabelText("Typ akce");
    fireEvent.change(select, { target: { value: "" } });
    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_FILTERS, actionType: "" });
  });

  it("shows 'Zrušit filtry' when only actionType is set and calls onClear when clicked", () => {
    const onClear = jest.fn();
    renderFilters({
      filters: { ...EMPTY_FILTERS, actionType: MarketingActionType.PR },
      onClear,
    });
    const clearBtn = screen.getByRole("button", { name: /Zrušit filtry/ });
    fireEvent.click(clearBtn);
    expect(onClear).toHaveBeenCalledTimes(1);
  });

  it("hides 'Zrušit filtry' when all filters are empty", () => {
    renderFilters();
    expect(screen.queryByRole("button", { name: /Zrušit filtry/ })).not.toBeInTheDocument();
  });

  it("EMPTY_FILTERS includes actionType as empty string", () => {
    expect(EMPTY_FILTERS).toEqual({
      searchText: "",
      dateFrom: "",
      dateTo: "",
      actionType: "",
    });
  });
});
```

- [ ] **Step 2: Run the test — it fails**

Run: `npm --prefix frontend test -- --testPathPattern=MarketingActionFilters.test`
Expected: FAIL — `getByLabelText("Typ akce")` finds no element; `EMPTY_FILTERS` shape mismatch.

- [ ] **Step 3: Commit the failing test**

```bash
git add frontend/src/components/marketing/list/__tests__/MarketingActionFilters.test.tsx
git commit -m "test: add failing MarketingActionFilters Typ akce dropdown spec"
```

---

## Task 9: Frontend — implement the Typ akce dropdown in `MarketingActionFilters`

**Files:**
- Modify: `frontend/src/components/marketing/list/MarketingActionFilters.tsx`

- [ ] **Step 1: Replace `MarketingActionFilters.tsx` with the extended version**

Replace the full contents of `frontend/src/components/marketing/list/MarketingActionFilters.tsx` with:

```tsx
import React from "react";
import { X } from "lucide-react";
import { MarketingActionType } from "../../../api/generated/api-client";
import {
  ACTION_TYPE_LABELS,
  ALL_ACTION_TYPE_OPTIONS,
} from "./marketingActionTypeLabels";

interface Filters {
  searchText: string;
  dateFrom: string;
  dateTo: string;
  actionType: MarketingActionType | "";
}

interface MarketingActionFiltersProps {
  filters: Filters;
  onChange: (filters: Filters) => void;
  onClear: () => void;
}

const EMPTY_FILTERS: Filters = {
  searchText: "",
  dateFrom: "",
  dateTo: "",
  actionType: "",
};

const hasActiveFilters = (f: Filters) =>
  f.searchText !== "" ||
  f.dateFrom !== "" ||
  f.dateTo !== "" ||
  f.actionType !== "";

const MarketingActionFilters: React.FC<MarketingActionFiltersProps> = ({
  filters,
  onChange,
  onClear,
}) => {
  const setText =
    (key: "searchText" | "dateFrom" | "dateTo") =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      onChange({ ...filters, [key]: e.target.value });

  const setActionType = (e: React.ChangeEvent<HTMLSelectElement>) =>
    onChange({
      ...filters,
      actionType: (e.target.value as MarketingActionType | ""),
    });

  return (
    <div className="flex flex-wrap gap-3 items-center p-4 bg-white border border-gray-200 rounded-lg">
      <select
        aria-label="Typ akce"
        value={filters.actionType}
        onChange={setActionType}
        className="border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
      >
        <option value="">Všechny typy</option>
        {ALL_ACTION_TYPE_OPTIONS.map((t) => (
          <option key={t} value={t}>
            {ACTION_TYPE_LABELS[t]}
          </option>
        ))}
      </select>
      <input
        type="text"
        placeholder="Hledat název..."
        value={filters.searchText}
        onChange={setText("searchText")}
        className="border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 min-w-[200px]"
      />
      <input
        type="date"
        value={filters.dateFrom}
        onChange={setText("dateFrom")}
        className="border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
        title="Od"
      />
      <span className="text-gray-400 text-sm">–</span>
      <input
        type="date"
        value={filters.dateTo}
        onChange={setText("dateTo")}
        className="border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
        title="Do"
      />
      {hasActiveFilters(filters) && (
        <button
          onClick={onClear}
          className="flex items-center gap-1 px-3 py-2 text-sm text-gray-600 hover:text-gray-900 border border-gray-300 rounded-md hover:bg-gray-50 transition-colors"
        >
          <X className="h-3 w-3" />
          Zrušit filtry
        </button>
      )}
    </div>
  );
};

export default MarketingActionFilters;
export { EMPTY_FILTERS };
export type { Filters as MarketingFilters };
```

- [ ] **Step 2: Run the filter component tests — should pass**

Run: `npm --prefix frontend test -- --testPathPattern=MarketingActionFilters.test`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/marketing/list/MarketingActionFilters.tsx
git commit -m "feat: add Typ akce dropdown to MarketingActionFilters"
```

---

## Task 10: Frontend — fix `MarketingCalendarPage` mock + integration

**Files:**
- Modify: `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx:89-94`
- Modify: `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx:69`

- [ ] **Step 1: Forward `actionType` from `filters` into `useMarketingActions`**

In `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`, replace the existing `useMarketingActions` call (currently lines 89–94) with:

```tsx
const listQuery = useMarketingActions({
  pageNumber,
  searchTerm: filters.searchText || undefined,
  actionType: filters.actionType || undefined,
  startDateFrom: filters.dateFrom ? new Date(filters.dateFrom) : undefined,
  startDateTo: filters.dateTo ? new Date(filters.dateTo) : undefined,
});
```

- [ ] **Step 2: Update the mocked `EMPTY_FILTERS` literal in the page test**

In `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`, replace the existing mock at line 69:

```ts
EMPTY_FILTERS: { searchText: "", dateFrom: "", dateTo: "" },
```

with:

```ts
EMPTY_FILTERS: { searchText: "", dateFrom: "", dateTo: "", actionType: "" },
```

- [ ] **Step 3: Type-check + run the page tests**

Run: `npm --prefix frontend test -- --testPathPattern=MarketingCalendarPage.test`
Expected: all existing tests still pass.

- [ ] **Step 4: Commit**

```bash
git add \
  frontend/src/components/marketing/pages/MarketingCalendarPage.tsx \
  frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx
git commit -m "feat: pass actionType filter to useMarketingActions on MarketingCalendarPage"
```

---

## Task 11: Frontend — replace broken label maps in `MarketingActionGrid`

**Background:** `MarketingActionGrid.tsx:20–36` currently defines `ACTION_TYPE_BADGE` and `ACTION_TYPE_LABELS` keyed by `General`, `Promotion`, `Launch`, `Campaign`, `Event`, `Other` — none of which (except `Event`) match the real `MarketingActionType` enum (`SocialMedia`, `Blog`, `Newsletter`, `PR`, `Event`, `Meeting`). The labels are also mis-assigned (`Promotion → "Událost"`, `Launch → "Email"`). The grid renders correctly today only because the lookup falls back to the raw `action.actionType` string for missing keys. The filter feature requires the canonical map, so we fix the grid and its test in the same task.

**Files:**
- Modify: `frontend/src/components/marketing/list/MarketingActionGrid.tsx:20-36, 111-121`
- Modify: `frontend/src/components/marketing/list/__tests__/MarketingActionGrid.test.tsx`

- [ ] **Step 1: Update the grid test fixtures + assertions**

Replace the entire contents of `frontend/src/components/marketing/list/__tests__/MarketingActionGrid.test.tsx` with:

```tsx
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import MarketingActionGrid from "../MarketingActionGrid";
import type { MarketingActionDto } from "../MarketingActionGrid";
import { MarketingActionType } from "../../../../api/generated/api-client";

const defaultProps = {
  actions: [],
  totalPages: 1,
  pageNumber: 1,
  onPageChange: jest.fn(),
  onActionClick: jest.fn(),
};

const sampleActions: MarketingActionDto[] = [
  {
    id: 1,
    title: "Letní kampaň",
    actionType: MarketingActionType.PR,
    dateFrom: "2026-06-01",
    dateTo: "2026-06-30",
    associatedProducts: ["AKL001", "AKL002"],
  },
  {
    id: 2,
    title: "Email newsletter",
    actionType: MarketingActionType.Newsletter,
    dateFrom: "2026-07-01",
    dateTo: "2026-07-15",
    associatedProducts: [],
  },
];

beforeEach(() => {
  jest.clearAllMocks();
});

describe("MarketingActionGrid — loading", () => {
  it("shows loading message when isLoading is true", () => {
    render(<MarketingActionGrid {...defaultProps} isLoading={true} />);
    expect(screen.getByText("Načítání...")).toBeInTheDocument();
  });

  it("does not render a table when loading", () => {
    render(<MarketingActionGrid {...defaultProps} isLoading={true} />);
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});

describe("MarketingActionGrid — empty state", () => {
  it("shows empty message when actions array is empty", () => {
    render(<MarketingActionGrid {...defaultProps} />);
    expect(
      screen.getByText("Žádné marketingové akce nebyly nalezeny."),
    ).toBeInTheDocument();
  });

  it("does not render a table when empty", () => {
    render(<MarketingActionGrid {...defaultProps} />);
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});

describe("MarketingActionGrid — table", () => {
  it("renders a row for each action", () => {
    render(<MarketingActionGrid {...defaultProps} actions={sampleActions} />);
    expect(screen.getByText("Letní kampaň")).toBeInTheDocument();
    expect(screen.getByText("Email newsletter")).toBeInTheDocument();
  });

  it("renders column headers", () => {
    render(<MarketingActionGrid {...defaultProps} actions={sampleActions} />);
    expect(screen.getByText("Název")).toBeInTheDocument();
    expect(screen.getByText("Typ")).toBeInTheDocument();
    expect(screen.getByText("Od")).toBeInTheDocument();
    expect(screen.getByText("Do")).toBeInTheDocument();
    expect(screen.getByText("Produkty")).toBeInTheDocument();
  });

  it("shows Czech label for PR action type", () => {
    render(<MarketingActionGrid {...defaultProps} actions={sampleActions} />);
    expect(screen.getByText("PR")).toBeInTheDocument();
  });

  it("shows Czech label for Newsletter action type", () => {
    render(<MarketingActionGrid {...defaultProps} actions={sampleActions} />);
    expect(screen.getByText("Newsletter")).toBeInTheDocument();
  });

  it("falls back to raw actionType when value is not in the enum", () => {
    const action: MarketingActionDto = {
      id: 99,
      title: "Neznámý typ",
      actionType: "Unknown",
      dateFrom: "2026-01-01",
      dateTo: "2026-01-31",
    };
    render(<MarketingActionGrid {...defaultProps} actions={[action]} />);
    expect(screen.getByText("Unknown")).toBeInTheDocument();
  });

  it("shows comma-separated associated products", () => {
    render(<MarketingActionGrid {...defaultProps} actions={sampleActions} />);
    expect(screen.getByText("AKL001, AKL002")).toBeInTheDocument();
  });

  it("shows dash when no associated products", () => {
    render(<MarketingActionGrid {...defaultProps} actions={sampleActions} />);
    const dashes = screen.getAllByText("—");
    expect(dashes.length).toBeGreaterThanOrEqual(1);
  });

  it("calls onActionClick with action id on row click", () => {
    const onActionClick = jest.fn();
    render(
      <MarketingActionGrid
        {...defaultProps}
        actions={sampleActions}
        onActionClick={onActionClick}
      />,
    );
    fireEvent.click(screen.getByText("Letní kampaň"));
    expect(onActionClick).toHaveBeenCalledWith(1);
  });

  it("calls onActionClick with second row id", () => {
    const onActionClick = jest.fn();
    render(
      <MarketingActionGrid
        {...defaultProps}
        actions={sampleActions}
        onActionClick={onActionClick}
      />,
    );
    fireEvent.click(screen.getByText("Email newsletter"));
    expect(onActionClick).toHaveBeenCalledWith(2);
  });
});

describe("MarketingActionGrid — pagination", () => {
  it("does not render pagination when totalPages is 1", () => {
    render(
      <MarketingActionGrid {...defaultProps} actions={sampleActions} totalPages={1} />,
    );
    expect(screen.queryByRole("button", { name: /chevron/i })).not.toBeInTheDocument();
  });

  it("renders pagination when totalPages > 1", () => {
    render(
      <MarketingActionGrid
        {...defaultProps}
        actions={sampleActions}
        totalPages={3}
        pageNumber={2}
      />,
    );
    expect(screen.getByText("2 / 3")).toBeInTheDocument();
  });

  it("disables prev button on first page", () => {
    render(
      <MarketingActionGrid
        {...defaultProps}
        actions={sampleActions}
        totalPages={3}
        pageNumber={1}
      />,
    );
    const buttons = screen.getAllByRole("button");
    const prevBtn = buttons.find((b) => b.querySelector("svg"));
    expect(prevBtn).toBeDisabled();
  });

  it("disables next button on last page", () => {
    render(
      <MarketingActionGrid
        {...defaultProps}
        actions={sampleActions}
        totalPages={3}
        pageNumber={3}
      />,
    );
    const buttons = screen.getAllByRole("button");
    const nextBtn = buttons[buttons.length - 1];
    expect(nextBtn).toBeDisabled();
  });

  it("calls onPageChange with page - 1 when prev is clicked", () => {
    const onPageChange = jest.fn();
    render(
      <MarketingActionGrid
        {...defaultProps}
        actions={sampleActions}
        totalPages={3}
        pageNumber={2}
        onPageChange={onPageChange}
      />,
    );
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[0]);
    expect(onPageChange).toHaveBeenCalledWith(1);
  });

  it("calls onPageChange with page + 1 when next is clicked", () => {
    const onPageChange = jest.fn();
    render(
      <MarketingActionGrid
        {...defaultProps}
        actions={sampleActions}
        totalPages={3}
        pageNumber={2}
        onPageChange={onPageChange}
      />,
    );
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[buttons.length - 1]);
    expect(onPageChange).toHaveBeenCalledWith(3);
  });
});

describe("MarketingActionGrid — OutlookSyncStatus badge", () => {
  it("renders red dot badge when outlookSyncStatus is 'Failed'", () => {
    const action: MarketingActionDto = {
      id: 10,
      title: "Akce se selháním",
      actionType: MarketingActionType.SocialMedia,
      dateFrom: "2026-03-01",
      dateTo: "2026-03-15",
      outlookSyncStatus: "Failed",
    };
    render(<MarketingActionGrid {...defaultProps} actions={[action]} />);
    const badge = screen.getByTitle(
      "Synchronizace s Outlookem selhala – bude opakována",
    );
    expect(badge).toBeInTheDocument();
    expect(badge.classList.contains("bg-red-500")).toBe(true);
  });

  it("does not render red dot badge when outlookSyncStatus is 'Synced'", () => {
    const action: MarketingActionDto = {
      id: 11,
      title: "Synchronizovaná akce",
      actionType: MarketingActionType.SocialMedia,
      dateFrom: "2026-03-01",
      dateTo: "2026-03-15",
      outlookSyncStatus: "Synced",
    };
    render(<MarketingActionGrid {...defaultProps} actions={[action]} />);
    expect(
      screen.queryByTitle("Synchronizace s Outlookem selhala – bude opakována"),
    ).not.toBeInTheDocument();
  });

  it("does not render red dot badge when outlookSyncStatus is undefined", () => {
    const action: MarketingActionDto = {
      id: 12,
      title: "Akce bez statusu",
      actionType: MarketingActionType.SocialMedia,
      dateFrom: "2026-03-01",
      dateTo: "2026-03-15",
    };
    render(<MarketingActionGrid {...defaultProps} actions={[action]} />);
    expect(
      screen.queryByTitle("Synchronizace s Outlookem selhala – bude opakována"),
    ).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the grid tests — confirm the rewritten suite fails (label map still wrong)**

Run: `npm --prefix frontend test -- --testPathPattern=MarketingActionGrid.test`
Expected: FAIL — `screen.getByText("Newsletter")` is satisfied by the raw fallback, but `screen.getByText("PR")` would also pass for the wrong reason (the existing broken map maps `Campaign → "PR"`, and `actionType` is now `"PR"` so the fallback prints `"PR"` too). The PR assertion may pass; the badge-class assertion in the OutlookSyncStatus test will fail because the grid still uses the old badge map keyed against `General` (so it falls back to `Other → bg-gray-100`) when the fixture's `actionType` is `SocialMedia`. The PR is to fix this in Step 3.

If unsure, run the full file and capture which tests fail before continuing.

- [ ] **Step 3: Replace the grid's local maps with imports from the shared module**

In `frontend/src/components/marketing/list/MarketingActionGrid.tsx`:

(a) Replace lines 20–36 (the two local `ACTION_TYPE_BADGE` and `ACTION_TYPE_LABELS` constant declarations) with an import directly under the existing imports at the top of the file:

```tsx
import {
  ACTION_TYPE_BADGE,
  ACTION_TYPE_LABELS,
} from "./marketingActionTypeLabels";
```

(b) Replace the badge/label render block (currently lines 111–121) with the version below. The fallback now uses Tailwind classes for an unknown key and prints the raw `actionType` string:

```tsx
<td className="px-4 py-3">
  <span
    className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
      ACTION_TYPE_BADGE[action.actionType as keyof typeof ACTION_TYPE_BADGE] ??
      "bg-gray-100 text-gray-800"
    }`}
  >
    {ACTION_TYPE_LABELS[action.actionType as keyof typeof ACTION_TYPE_LABELS] ??
      action.actionType}
  </span>
</td>
```

(c) Remove any now-unused references to `ACTION_TYPE_BADGE.Other` in the file — the new fallback is an inline string.

- [ ] **Step 4: Re-run the grid tests — should pass**

Run: `npm --prefix frontend test -- --testPathPattern=MarketingActionGrid.test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add \
  frontend/src/components/marketing/list/MarketingActionGrid.tsx \
  frontend/src/components/marketing/list/__tests__/MarketingActionGrid.test.tsx
git commit -m "fix: align MarketingActionGrid label/badge map with real MarketingActionType enum"
```

---

## Task 12: Full validation

- [ ] **Step 1: Backend build, format, and full test run**

Run: `dotnet format backend/Anela.Heblo.sln && dotnet build backend/Anela.Heblo.sln && dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~Marketing"`
Expected: build succeeds with 0 errors, all Marketing-scoped tests pass.

- [ ] **Step 2: Frontend build, lint, and touched-file tests**

Run: `npm --prefix frontend run build && npm --prefix frontend run lint && npm --prefix frontend test -- --testPathPattern="(MarketingActionFilters|MarketingActionGrid|MarketingCalendarPage)"`
Expected: build/lint pass; all three test files green.

- [ ] **Step 3: Manual smoke (browser)**

Start the local backend and frontend (per `docs/development/setup.md`), open the Marketing calendar page, switch to **Seznam**, and verify:
- The "Typ akce" dropdown is the leftmost control.
- Selecting "Blog" reduces the grid to Blog rows; the URL `?ActionType=Blog` is the network call (DevTools Network tab).
- Selecting "Všechny typy" restores the full list (no `ActionType` query param).
- The "Zrušit filtry" button appears when "Typ akce" is the only active filter and clears it.

Document any deviation in the PR description (this is a manual UX check, not a hard gate, but listed for the engineer's awareness).

- [ ] **Step 4: Final commit (only if there are any uncommitted formatting changes)**

```bash
git status
# If anything is uncommitted from `dotnet format`, stage and commit it:
git add -A && git commit -m "chore: apply dotnet format after marketing actionType filter"
```

If nothing is pending, skip this step.

---

## Self-review checklist (run mentally before declaring done)

- **Spec FR-1** (`ActionType` on request): Task 2 ✓
- **Spec FR-2** (handler mapping): Task 3 ✓
- **Spec FR-3** (HTTP query parameter binding + 400): Task 4 ✓
- **Spec FR-4** (frontend dropdown, label centralization, reset behaviour): Tasks 6, 8, 9 ✓
- **Spec FR-5** (no repository change): respected — no edits to `MarketingActionRepository.cs` ✓
- **Spec NFR-1** (perf — existing index, no migration): respected ✓
- **Spec NFR-2** (security — typed enum, no new authZ): respected ✓
- **Spec NFR-3** (backwards compatibility — omitting param is no-op): handler covers null path; integration test covers happy path ✓
- **Spec NFR-4** (testing — handler, controller binding, filter component, grid map regression): Tasks 1, 4, 8, 11 ✓
- **arch-review Decision 1** (label module location): Task 6 ✓
- **arch-review Decision 2** (wire existing path): respected ✓
- **arch-review Decision 3** (typed enum DTO): Task 2 ✓
- **arch-review Decision 4** (component state only): Task 10 keeps existing pattern ✓
- **arch-review Risk: broken label map**: Task 11 rebuilds map and updates fixtures ✓
- **arch-review Risk: positional client signature drift**: Task 5 Step 2 verifies; Task 7 mirrors regenerated order ✓
- **arch-review Risk: stale `EMPTY_FILTERS` mock**: Task 10 Step 2 ✓
