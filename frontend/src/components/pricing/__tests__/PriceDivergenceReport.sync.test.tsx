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

// What the real `mutateAsync` resolves: the rows as they stand after the writes, plus what
// the sync did to the live ERP.
const outcome = (syncedRows = rows, writtenCount = 0, failedCount = 0, remainingCount = 0) => ({
  rows: syncedRows,
  writtenCount,
  failedCount,
  remainingCount,
});

interface RenderOptions {
  syncPrices?: jest.Mock;
  isSyncing?: boolean;
  canWrite?: boolean;
}

const renderReport = ({
  syncPrices = jest.fn().mockResolvedValue(outcome()),
  isSyncing = false,
  canWrite = true,
}: RenderOptions = {}) => {
  mockUsePriceDivergenceReport.mockReturnValue({ data: { rows, summary }, isLoading: false, error: null });
  mockUseSetProductPrice.mockReturnValue({ mutateAsync: jest.fn(), isPending: false });
  mockUseSyncProductPrices.mockReturnValue({ mutateAsync: syncPrices, isPending: isSyncing });

  render(<PriceDivergenceReport canWrite={canWrite} />);
  return { syncPrices };
};

const syncButton = () => screen.getByTestId("sync-prices-button");

// The sync writes into the live ERP, so every run goes through the operator's confirmation.
let confirmSpy: jest.SpyInstance;

beforeEach(() => {
  jest.clearAllMocks();
  confirmSpy = jest.spyOn(window, "confirm").mockReturnValue(true);
});

afterEach(() => {
  confirmSpy.mockRestore();
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

// The button is the only thing on this screen that writes prices in bulk, so a click must
// never reach the live ERP without the operator saying so.
test("writes nothing when the operator cancels the confirmation", async () => {
  // Arrange
  confirmSpy.mockReturnValue(false);
  const { syncPrices } = renderReport();

  // Act
  await userEvent.click(syncButton());

  // Assert
  expect(syncPrices).not.toHaveBeenCalled();
});

test("names the live ERP and how many prices will be overwritten before writing", async () => {
  // Arrange — one of the two rows diverges, so only that one would be written
  const divergentRows = [
    makeRow("MAS001180", "Maska", PriceDivergenceKind.FlexiDiffers),
    makeRow("TON002030", "Tonikum"),
  ];
  mockUsePriceDivergenceReport.mockReturnValue({
    data: { rows: divergentRows, summary },
    isLoading: false,
    error: null,
  });
  mockUseSetProductPrice.mockReturnValue({ mutateAsync: jest.fn(), isPending: false });
  mockUseSyncProductPrices.mockReturnValue({
    mutateAsync: jest.fn().mockResolvedValue(outcome(divergentRows, 1)),
    isPending: false,
  });
  render(<PriceDivergenceReport canWrite />);

  // Act
  await userEvent.click(syncButton());

  // Assert
  expect(confirmSpy.mock.calls[0][0]).toContain("živého ERP Flexi");
  expect(confirmSpy.mock.calls[0][0]).toContain("(nyní 1)");
});

// The read-only banner promises this screen writes nowhere. A sync button under it would
// make that a lie, and the endpoint would refuse the call anyway.
test("offers no sync at all to an operator who cannot write prices", () => {
  // Arrange & Act
  renderReport({ canWrite: false });

  // Assert
  expect(screen.queryByTestId("sync-prices-button")).not.toBeInTheDocument();
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
    .mockResolvedValueOnce(outcome([]));
  renderReport({ syncPrices });

  // Act
  await userEvent.click(syncButton());
  await screen.findByRole("alert");
  await userEvent.click(syncButton());

  // Assert
  await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
});
