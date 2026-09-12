import React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import PriceDivergenceReport from "../PriceDivergenceReport";
import * as hooks from "../../../api/hooks/useProductPricing";
import { PriceDivergenceKind } from "../../../api/generated/api-client";

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

const makeRow = (productCode: string, productName: string, kind = PriceDivergenceKind.InAgreement) => ({
  productCode,
  productName,
  shoptetPriceWithVat: 390.0,
  flexiPriceWithVat: 390.0,
  flexiPriceWithoutVat: 322.31,
  flexiPriceType: "bezDph",
  differenceWithVat: 0,
  differencePercent: 0,
  kind,
});

const summary = {
  totalInScope: 2,
  inAgreementCount: 2,
  flexiDiffersCount: 0,
  missingInShoptetCount: 0,
  missingInFlexiCount: 0,
  flexiPriceTypeUnknownCount: 0,
};

const rows = [makeRow("MAS001180", "Maska"), makeRow("TON002030", "Tonikum")];

interface RenderOptions {
  syncPrices?: jest.Mock;
  isSyncing?: boolean;
}

// The default resolves rows, as the real `mutateAsync` does — the component compares what
// came back against what it was showing to report how much the sync changed.
const renderReport = ({ syncPrices = jest.fn().mockResolvedValue(rows), isSyncing = false }: RenderOptions = {}) => {
  mockUsePriceDivergenceReport.mockReturnValue({ data: { rows, summary }, isLoading: false, error: null });
  mockUseSetProductPrice.mockReturnValue({ mutateAsync: jest.fn(), isPending: false });
  mockUseSyncProductPrices.mockReturnValue({ mutateAsync: syncPrices, isPending: isSyncing });

  render(<PriceDivergenceReport canWrite />);
  return { syncPrices };
};

const syncButton = () => screen.getByTestId("sync-prices-button");

beforeEach(() => {
  jest.clearAllMocks();
});

test("names the number of products the sync will cover", () => {
  // Arrange & Act
  renderReport();

  // Assert
  expect(syncButton()).toHaveTextContent("Synchronizovat (2)");
});

test("syncs every visible product when no filter narrows the table", async () => {
  // Arrange
  const { syncPrices } = renderReport();

  // Act
  await userEvent.click(syncButton());

  // Assert
  expect(syncPrices).toHaveBeenCalledWith(["MAS001180", "TON002030"]);
});

// The whole point of the feature: the operator filters down to what they care about, and
// the sync must not go and re-read the rest of the catalogue from two live systems.
test("syncs only the products left visible by the filters", async () => {
  // Arrange
  const { syncPrices } = renderReport();

  // Act — filter by name, which only applies on Enter
  await userEvent.type(screen.getByPlaceholderText("Název produktu..."), "Tonikum{Enter}");
  await userEvent.click(syncButton());

  // Assert
  expect(syncButton()).toHaveTextContent("Synchronizovat (1)");
  expect(syncPrices).toHaveBeenCalledWith(["TON002030"]);
});

test("disables the sync while a sync is already running", () => {
  // Arrange & Act
  renderReport({ isSyncing: true });

  // Assert
  expect(syncButton()).toBeDisabled();
});

test("disables the sync when the filters leave nothing to sync", async () => {
  // Arrange
  renderReport();

  // Act
  await userEvent.type(screen.getByPlaceholderText("Kód produktu..."), "NOSUCHCODE{Enter}");

  // Assert
  expect(syncButton()).toBeDisabled();
  expect(syncButton()).toHaveTextContent("Synchronizovat (0)");
});

test("announces a failed sync without disturbing the table", async () => {
  // Arrange
  const syncPrices = jest.fn().mockRejectedValue(new Error("boom"));
  renderReport({ syncPrices });

  // Act
  await userEvent.click(syncButton());

  // Assert
  expect(await screen.findByRole("alert")).toHaveTextContent("Ceny se nepodařilo synchronizovat.");
  expect(screen.getByText("Maska")).toBeInTheDocument();
});

test("clears a previous sync failure once a later sync succeeds", async () => {
  // Arrange
  const syncPrices = jest
    .fn()
    .mockRejectedValueOnce(new Error("boom"))
    .mockResolvedValueOnce([]);
  renderReport({ syncPrices });

  // Act
  await userEvent.click(syncButton());
  await screen.findByRole("alert");
  await userEvent.click(syncButton());

  // Assert
  await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
});
