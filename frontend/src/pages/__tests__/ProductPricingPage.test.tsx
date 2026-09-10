import React from "react";
import { render, screen } from "@testing-library/react";
import ProductPricingPage from "../ProductPricingPage";
import { usePermissionsContext } from "../../auth/PermissionsContext";
import { PriceDivergenceKind } from "../../api/generated/api-client";

// `mock`-prefixed so the jest.mock factory below may reference it; only read lazily, when
// the hook is called during render.
const mockDivergenceRow = {
  productCode: "MAS001180",
  productName: "Maska",
  shoptetPriceWithVat: 390.0,
  flexiPriceWithVat: 390.0,
  flexiPriceWithoutVat: 322.31,
  flexiPriceType: "bezDph",
  differenceWithVat: 0,
  differencePercent: 0,
  kind: PriceDivergenceKind.InAgreement,
};

jest.mock("../../api/hooks/useProductPricing", () => ({
  usePriceDivergenceReport: () => ({
    data: { rows: [mockDivergenceRow], summary: undefined },
    isLoading: false,
    error: null,
  }),
  useSetProductPrice: () => ({
    mutateAsync: jest.fn(),
    isPending: false,
  }),
}));

jest.mock("../../auth/PermissionsContext", () => ({
  usePermissionsContext: jest.fn(),
}));

const mockUsePermissionsContext = usePermissionsContext as jest.Mock;

const EDIT_AFFORDANCE_LABEL = `Upravit cenu ${mockDivergenceRow.productName}`;

beforeEach(() => {
  mockUsePermissionsContext.mockReturnValue({
    hasPermission: () => false,
  });
});

test("renders the page header", () => {
  // Act
  render(<ProductPricingPage />);

  // Assert
  expect(screen.getByRole("heading", { name: "Ceny produktů" })).toBeInTheDocument();
});

test("renders the Shoptet-vs-Flexi comparison table", () => {
  // Act
  render(<ProductPricingPage />);

  // Assert
  expect(screen.getByRole("table")).toBeInTheDocument();
});

test("passes write permission down so the operator can edit prices", () => {
  // Arrange
  mockUsePermissionsContext.mockReturnValue({
    hasPermission: (permission: string) => permission === "products.catalog.write",
  });

  // Act
  render(<ProductPricingPage />);

  // Assert — the edit affordance itself must appear, not merely the table.
  expect(
    screen.getByRole("button", { name: EDIT_AFFORDANCE_LABEL, exact: true }),
  ).toBeInTheDocument();
});

test("withholds the edit affordance from a read-only viewer", () => {
  // Arrange: the default beforeEach grants no permission.

  // Act
  render(<ProductPricingPage />);

  // Assert — this is the half that makes the test above mean something: an always-visible
  // pencil would otherwise satisfy the writer case too.
  expect(
    screen.queryByRole("button", { name: EDIT_AFFORDANCE_LABEL, exact: true }),
  ).not.toBeInTheDocument();
});

test("asks the permission system specifically for the catalog write permission", () => {
  // Arrange
  const hasPermission = jest.fn().mockReturnValue(false);
  mockUsePermissionsContext.mockReturnValue({ hasPermission });

  // Act
  render(<ProductPricingPage />);

  // Assert
  expect(hasPermission).toHaveBeenCalledWith("products.catalog.write");
});
