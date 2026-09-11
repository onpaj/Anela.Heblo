import React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import PriceDivergenceReport from "../PriceDivergenceReport";
import * as hooks from "../../../api/hooks/useProductPricing";
import { PriceDivergenceKind } from "../../../api/generated/api-client";

// A sync that re-reads two live systems and finds nothing changed leaves the table byte-for-byte
// identical. Without a status line that outcome is indistinguishable from a dead button, which is
// exactly how this screen was reported as broken.
jest.mock("../../../api/hooks/useProductPricing", () => {
  const actual = jest.requireActual("../../../api/hooks/useProductPricing");
  return {
    ...actual,
    usePriceDivergenceReport: jest.fn(),
    useSetProductPrice: jest.fn(),
    useSyncProductPrices: jest.fn(),
  };
});

const mockUsePriceDivergenceReport = hooks.usePriceDivergenceReport as jest.Mock;
const mockUseSetProductPrice = hooks.useSetProductPrice as jest.Mock;
const mockUseSyncProductPrices = hooks.useSyncProductPrices as jest.Mock;

const makeRow = (productCode: string, productName: string, shoptetPriceWithVat: number) => ({
  productCode,
  productName,
  shoptetPriceWithVat,
  flexiPriceWithVat: shoptetPriceWithVat,
  flexiPriceWithoutVat: 322.31,
  flexiPriceType: "bezDph",
  differenceWithVat: 0,
  differencePercent: 0,
  kind: PriceDivergenceKind.InAgreement,
});

const rows = [makeRow("MAS001180", "Maska", 390), makeRow("TON002030", "Tonikum", 250)];

const summary = {
  totalInScope: 2,
  inAgreementCount: 2,
  flexiDiffersCount: 0,
  missingInShoptetCount: 0,
  missingInFlexiCount: 0,
  flexiPriceTypeUnknownCount: 0,
};

const renderReport = (syncPrices: jest.Mock) => {
  mockUsePriceDivergenceReport.mockReturnValue({ data: { rows, summary }, isLoading: false, error: null });
  mockUseSetProductPrice.mockReturnValue({ mutateAsync: jest.fn(), isPending: false });
  mockUseSyncProductPrices.mockReturnValue({ mutateAsync: syncPrices, isPending: false });

  render(<PriceDivergenceReport canWrite />);
};

const syncButton = () => screen.getByTestId("sync-prices-button");
const syncStatus = () => screen.queryByTestId("sync-prices-status");

beforeEach(() => {
  jest.clearAllMocks();
});

test("shows no sync status before the operator has synced anything", () => {
  // Arrange & Act
  renderReport(jest.fn());

  // Assert
  expect(syncStatus()).not.toBeInTheDocument();
});

test("confirms a sync that changed nothing, so an unchanged table is not a dead button", async () => {
  // Arrange — the backend returns exactly the rows the report already holds
  renderReport(jest.fn().mockResolvedValue(rows));

  // Act
  await userEvent.click(syncButton());

  // Assert
  const status = await screen.findByTestId("sync-prices-status");
  expect(status).toHaveTextContent(/Synchronizováno v \d{1,2}:\d{2}/);
  expect(status).toHaveTextContent("beze změn");
});

test("names how many rows the sync actually changed", async () => {
  // Arrange
  const syncedRows = [makeRow("MAS001180", "Maska", 420), makeRow("TON002030", "Tonikum", 250)];
  renderReport(jest.fn().mockResolvedValue(syncedRows));

  // Act
  await userEvent.click(syncButton());

  // Assert
  expect(await screen.findByTestId("sync-prices-status")).toHaveTextContent("1 řádek se změnil");
});

test("uses the Czech plural the count calls for", async () => {
  // Arrange
  const syncedRows = [makeRow("MAS001180", "Maska", 420), makeRow("TON002030", "Tonikum", 270)];
  renderReport(jest.fn().mockResolvedValue(syncedRows));

  // Act
  await userEvent.click(syncButton());

  // Assert
  expect(await screen.findByTestId("sync-prices-status")).toHaveTextContent("2 řádky se změnily");
});

// A stale "Synchronizováno v 16:42" sitting next to a failure alert reads as if the sync had
// both succeeded and failed.
test("drops the previous confirmation when a later sync fails", async () => {
  // Arrange
  const syncPrices = jest.fn().mockResolvedValueOnce(rows).mockRejectedValueOnce(new Error("boom"));
  renderReport(syncPrices);

  // Act
  await userEvent.click(syncButton());
  await screen.findByTestId("sync-prices-status");
  await userEvent.click(syncButton());

  // Assert
  expect(await screen.findByRole("alert")).toHaveTextContent("Ceny se nepodařilo synchronizovat.");
  await waitFor(() => expect(syncStatus()).not.toBeInTheDocument());
});

// The line has to reach a screen reader: for an operator who cannot see the table stay the
// same, it is the only evidence the button did anything at all.
test("announces the confirmation to assistive technology", async () => {
  // Arrange
  renderReport(jest.fn().mockResolvedValue(rows));

  // Act
  await userEvent.click(syncButton());

  // Assert
  expect(await screen.findByRole("status")).toHaveTextContent("beze změn");
});
