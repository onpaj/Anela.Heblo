import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import PriceDivergenceReport from "../PriceDivergenceReport";
import * as hooks from "../../../api/hooks/useProductPricing";
import { PriceDivergenceKind } from "../../../api/generated/api-client";

jest.mock("../../../api/hooks/useProductPricing", () => {
  const actual = jest.requireActual("../../../api/hooks/useProductPricing");
  return {
    ...actual,
    usePriceDivergenceReport: jest.fn(),
  };
});

const mockUsePriceDivergenceReport = hooks.usePriceDivergenceReport as jest.Mock;

const inAgreementRow = {
  productCode: "MAS001180",
  productName: "Maska",
  shoptetPriceWithVat: 390.0,
  flexiPriceWithVat: 390.0,
  flexiPriceWithoutVat: 322.31,
  flexiPriceType: "bezDph",
  hebloMasterPriceWithVat: 390.0,
  differenceWithVat: 0,
  differencePercent: 0,
  kind: PriceDivergenceKind.InAgreement,
};

const divergentRow = {
  productCode: "TON002030",
  productName: "Tonikum",
  shoptetPriceWithVat: 390.0,
  flexiPriceWithVat: 447.7,
  flexiPriceWithoutVat: 370.0,
  flexiPriceType: "bezDph",
  hebloMasterPriceWithVat: 390.0,
  differenceWithVat: 57.7,
  differencePercent: 14.79,
  kind: PriceDivergenceKind.FlexiDiffers,
};

const sampleSummary = {
  totalInScope: 2,
  inAgreementCount: 1,
  flexiDiffersCount: 1,
  missingInShoptetCount: 0,
  missingInFlexiCount: 0,
  flexiPriceTypeUnknownCount: 0,
};

beforeEach(() => {
  jest.clearAllMocks();
});

const setData = (rows: any[], summary = sampleSummary) => {
  mockUsePriceDivergenceReport.mockReturnValue({
    data: { rows, summary },
    isLoading: false,
    error: null,
  });
};

test("renders the summary counts prominently", () => {
  // Arrange
  setData([inAgreementRow, divergentRow]);

  // Act
  render(<PriceDivergenceReport />);

  // Assert
  const summary = screen.getByTestId("divergence-summary");
  expect(summary).toHaveTextContent("2");
  expect(summary).toHaveTextContent("1");
});

test("renders a row per product returned by the report", () => {
  // Arrange
  setData([inAgreementRow, divergentRow]);

  // Act
  render(<PriceDivergenceReport />);

  // Assert
  expect(screen.getByText("MAS001180")).toBeInTheDocument();
  expect(screen.getByText("TON002030")).toBeInTheDocument();
});

test("the divergent-only filter hides rows that are in agreement", () => {
  // Arrange
  setData([inAgreementRow, divergentRow]);
  render(<PriceDivergenceReport />);
  expect(screen.getByText("MAS001180")).toBeInTheDocument();

  // Act
  fireEvent.click(screen.getByRole("checkbox", { name: "Zobrazit pouze rozdílné", exact: true }));

  // Assert
  expect(screen.queryByText("MAS001180")).not.toBeInTheDocument();
  expect(screen.getByText("TON002030")).toBeInTheDocument();
});

test("unchecking the divergent-only filter shows every row again", () => {
  // Arrange
  setData([inAgreementRow, divergentRow]);
  render(<PriceDivergenceReport />);
  const checkbox = screen.getByRole("checkbox", { name: "Zobrazit pouze rozdílné", exact: true });

  // Act
  fireEvent.click(checkbox);
  fireEvent.click(checkbox);

  // Assert
  expect(screen.getByText("MAS001180")).toBeInTheDocument();
  expect(screen.getByText("TON002030")).toBeInTheDocument();
});

test("makes clear the view is read-only", () => {
  // Arrange
  setData([inAgreementRow]);

  // Act
  render(<PriceDivergenceReport />);

  // Assert
  expect(screen.getByTestId("divergence-readonly-banner")).toHaveTextContent(/pouze čtení/i);
});

test("shows a loading state while the report is fetching", () => {
  // Arrange
  mockUsePriceDivergenceReport.mockReturnValue({ data: undefined, isLoading: true, error: null });

  // Act
  render(<PriceDivergenceReport />);

  // Assert
  expect(screen.getByText(/Načítání kontroly cen/)).toBeInTheDocument();
});

test("shows an error state when the report fails to load", () => {
  // Arrange
  mockUsePriceDivergenceReport.mockReturnValue({
    data: undefined,
    isLoading: false,
    error: new Error("network down"),
  });

  // Act
  render(<PriceDivergenceReport />);

  // Assert
  expect(screen.getByText(/network down/)).toBeInTheDocument();
});
