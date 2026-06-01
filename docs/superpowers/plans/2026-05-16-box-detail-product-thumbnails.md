# Box Detail Product Thumbnails Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a 48×48 product thumbnail inline next to the product name in the transport box items table.

**Architecture:** The backend already sends `imageUrl` on each `TransportBoxItemDto`. The only change is in `TransportBoxItems.tsx`: wrap the name cell content in a flex row — image/placeholder on the left, name + lot/exp text on the right. No backend work needed.

**Tech Stack:** React, TypeScript, Tailwind CSS, Jest + React Testing Library

---

### Task 1: Write failing tests for thumbnail display

**Files:**
- Create: `frontend/src/components/transport/box-detail/__tests__/TransportBoxItems.test.tsx`

- [ ] **Step 1: Create the test file**

```tsx
import React from "react";
import { render, screen } from "@testing-library/react";
import TransportBoxItems from "../TransportBoxItems";
import { TransportBoxDto, TransportBoxItemDto } from "../../../../api/generated/api-client";

jest.mock("../../../../api/hooks/useManufacturedProductInventory", () => ({
  useManufacturedProductInventoryQuery: jest.fn(() => ({
    data: { items: [] },
    isLoading: false,
    error: null,
  })),
}));

jest.mock("../../../common/CatalogAutocomplete", () => ({
  CatalogAutocomplete: () => null,
}));

function makeItem(overrides: Partial<TransportBoxItemDto> = {}): TransportBoxItemDto {
  const item = new TransportBoxItemDto();
  item.id = 1;
  item.productCode = "P001";
  item.productName = "Test Product";
  item.amount = 10;
  item.imageUrl = undefined;
  item.dateAdded = new Date("2024-01-01T10:00:00Z");
  item.userAdded = "Jan Novák";
  return Object.assign(item, overrides);
}

function makeBox(items: TransportBoxItemDto[]): TransportBoxDto {
  const box = new TransportBoxDto();
  box.id = 1;
  box.code = "B001";
  box.state = "New";
  box.items = items;
  box.stateLog = [];
  box.allowedTransitions = [];
  return box;
}

const defaultProps = {
  isFormEditable: jest.fn(() => false),
  formatDate: () => "01.01.2024",
  handleRemoveItem: jest.fn(),
  quantityInput: "",
  setQuantityInput: jest.fn(),
  selectedProduct: null,
  setSelectedProduct: jest.fn(),
  handleAddItem: jest.fn(),
  handleAddManufacturedItem: jest.fn(),
  lastAddedItem: null,
  handleQuickAdd: jest.fn(),
  lastManufacturedItems: [],
};

describe("TransportBoxItems — product thumbnails", () => {
  it("renders an img element when item has imageUrl", () => {
    const item = makeItem({ imageUrl: "https://example.com/product.jpg" });
    render(<TransportBoxItems {...defaultProps} transportBox={makeBox([item])} />);

    const img = screen.getByRole("img");
    expect(img).toBeInTheDocument();
    expect(img).toHaveAttribute("src", "https://example.com/product.jpg");
    expect(img).toHaveAttribute("alt", "Test Product");
  });

  it("renders a grey placeholder when imageUrl is undefined", () => {
    const item = makeItem({ imageUrl: undefined });
    render(<TransportBoxItems {...defaultProps} transportBox={makeBox([item])} />);

    expect(screen.queryByRole("img")).not.toBeInTheDocument();
    expect(document.querySelector(".bg-gray-200")).toBeInTheDocument();
  });

  it("still shows product name and code alongside the image", () => {
    const item = makeItem({ imageUrl: "https://example.com/product.jpg" });
    render(<TransportBoxItems {...defaultProps} transportBox={makeBox([item])} />);

    expect(screen.getByText("Test Product")).toBeInTheDocument();
    expect(screen.getByText("P001")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the tests to confirm they fail**

```bash
cd frontend && npx jest --testPathPattern="TransportBoxItems.test" --no-coverage 2>&1 | tail -20
```

Expected: 3 failing tests — the component renders no `<img>` and no placeholder yet.

---

### Task 2: Implement thumbnail display in the Name cell

**Files:**
- Modify: `frontend/src/components/transport/box-detail/TransportBoxItems.tsx` (lines 362–381)

- [ ] **Step 3: Replace the Name `<td>` content with a flex row**

Find this block inside the `transportBox.items.map` tbody (around line 369):

```tsx
<td className="px-2 py-2 text-sm text-gray-900">
  <div className="truncate" title={item.productName || "-"}>
    {item.productName || "-"}
  </div>
  {(item.lotNumber || item.expirationDate) && (
    <div className="text-xs text-gray-500 mt-0.5 flex gap-3">
      {item.lotNumber && <span>Lot: {item.lotNumber}</span>}
      {item.expirationDate && (
        <span>Exp: {item.expirationDate.toISOString().slice(0, 10)}</span>
      )}
    </div>
  )}
</td>
```

Replace it with:

```tsx
<td className="px-2 py-2 text-sm text-gray-900">
  <div className="flex items-center gap-2">
    {item.imageUrl ? (
      <img
        src={item.imageUrl}
        alt={item.productName || item.productCode || ""}
        className="w-12 h-12 object-cover rounded flex-shrink-0"
        onError={(e) => {
          const target = e.currentTarget;
          target.style.display = "none";
          const placeholder = target.nextElementSibling as HTMLElement | null;
          if (placeholder) placeholder.style.display = "block";
        }}
      />
    ) : null}
    <div
      className="w-12 h-12 bg-gray-200 rounded flex-shrink-0"
      style={{ display: item.imageUrl ? "none" : "block" }}
    />
    <div className="min-w-0">
      <div className="truncate" title={item.productName || "-"}>
        {item.productName || "-"}
      </div>
      {(item.lotNumber || item.expirationDate) && (
        <div className="text-xs text-gray-500 mt-0.5 flex gap-3">
          {item.lotNumber && <span>Lot: {item.lotNumber}</span>}
          {item.expirationDate && (
            <span>Exp: {item.expirationDate.toISOString().slice(0, 10)}</span>
          )}
        </div>
      )}
    </div>
  </div>
</td>
```

- [ ] **Step 4: Run the tests to confirm they pass**

```bash
cd frontend && npx jest --testPathPattern="TransportBoxItems.test" --no-coverage 2>&1 | tail -20
```

Expected: 3 passing tests.

- [ ] **Step 5: Run the full frontend build and lint**

```bash
cd frontend && npm run build 2>&1 | tail -20 && npm run lint 2>&1 | tail -20
```

Expected: no errors.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/transport/box-detail/__tests__/TransportBoxItems.test.tsx \
        frontend/src/components/transport/box-detail/TransportBoxItems.tsx
git commit -m "feat(transport): show product thumbnails in box detail items table"
```
