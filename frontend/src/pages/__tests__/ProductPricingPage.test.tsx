import React from "react";
import { render, screen } from "@testing-library/react";
import ProductPricingPage from "../ProductPricingPage";

jest.mock("../../api/hooks/useProductPricing", () => ({
  usePriceDivergenceReport: () => ({
    data: { rows: [], summary: undefined },
    isLoading: false,
    error: null,
  }),
}));

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
