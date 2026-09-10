import React from "react";
import { render, screen } from "@testing-library/react";
import ProductPricingPage from "../ProductPricingPage";
import { usePermissionsContext } from "../../auth/PermissionsContext";

jest.mock("../../api/hooks/useProductPricing", () => ({
  usePriceDivergenceReport: () => ({
    data: { rows: [], summary: undefined },
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

  // Assert — no rows are rendered, so we only assert the page renders without throwing
  // when write access is granted (row-level edit affordances are covered in
  // PriceDivergenceReport.test.tsx).
  expect(screen.getByRole("table")).toBeInTheDocument();
});
