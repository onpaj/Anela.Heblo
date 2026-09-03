import React from "react";
import { render, screen, fireEvent, within } from "@testing-library/react";
import ProductPricingPage from "../ProductPricingPage";

const mockSetPrice = jest.fn();
const mockResolveConflict = jest.fn();
const mockTriggerSync = jest.fn();
let mockPrices: any[] = [];

jest.mock("../../api/hooks/useProductPricing", () => ({
  useProductPrices: () => ({ data: mockPrices, isLoading: false, error: null }),
  useSetProductPrice: () => ({ mutate: mockSetPrice, isPending: false }),
  usePriceSyncConflicts: () => ({ data: [], isLoading: false, error: null }),
  useResolvePriceConflict: () => ({ mutate: mockResolveConflict, isPending: false }),
  useTriggerPriceSync: () => ({ mutate: mockTriggerSync, isPending: false }),
}));

// Shell components read these contexts; without mocks the page fails to render.
jest.mock("../../auth/useAuth", () => ({ useAuth: () => ({ user: { name: "Test" } }) }));
jest.mock("../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => ({ hasPermission: () => true }),
}));

const inSyncRow = {
  productCode: "OCH001030",
  productName: "Olej na obličej",
  priceWithVat: 190,
  priceWithoutVat: 157.02,
  vatRate: 21,
  modifiedAt: "2026-09-03T10:00:00",
  shoptetStatus: "InSync",
  shoptetRemoteValue: null,
  flexiStatus: "InSync",
  flexiRemoteValue: null,
};

const conflictedRow = {
  ...inSyncRow,
  productCode: "TON002030",
  productName: "Tonikum",
  priceWithVat: 210,
  flexiStatus: "Conflict",
  flexiRemoteValue: 175,
};

beforeEach(() => {
  jest.clearAllMocks();
  mockPrices = [inSyncRow];
});

test("renders a row per product with its price and both sync statuses", () => {
  // Arrange
  mockPrices = [inSyncRow, conflictedRow];

  // Act
  render(<ProductPricingPage />);

  // Assert
  expect(screen.getByText("OCH001030")).toBeInTheDocument();
  expect(screen.getByText("TON002030")).toBeInTheDocument();
  expect(screen.getAllByTestId("sync-status-shoptet")).toHaveLength(2);
  expect(screen.getAllByTestId("sync-status-flexi")).toHaveLength(2);
});

test("saving an inline edit sends the new price", () => {
  // Arrange
  render(<ProductPricingPage />);
  const input = screen.getByLabelText("Cena s DPH pro OCH001030");

  // Act
  fireEvent.change(input, { target: { value: "210" } });
  fireEvent.blur(input);

  // Assert
  expect(mockSetPrice).toHaveBeenCalledWith(
    expect.objectContaining({ productCode: "OCH001030", priceWithVat: 210 }),
  );
});

test("a conflicted row shows both values and the two resolution actions", () => {
  // Arrange
  mockPrices = [conflictedRow];

  // Act
  render(<ProductPricingPage />);
  const banner = screen.getByTestId("price-conflict-TON002030-Flexi");

  // Assert
  expect(within(banner).getByText(/210/)).toBeInTheDocument();
  expect(within(banner).getByText(/175/)).toBeInTheDocument();
  expect(within(banner).getByRole("button", { name: "Ponechat cenu z Hebla", exact: true })).toBeInTheDocument();
  expect(within(banner).getByRole("button", { name: "Převzít externí cenu", exact: true })).toBeInTheDocument();
});

test("accepting the remote price resolves the conflict with AcceptRemotePrice", () => {
  // Arrange
  mockPrices = [conflictedRow];
  render(<ProductPricingPage />);
  const banner = screen.getByTestId("price-conflict-TON002030-Flexi");

  // Act
  fireEvent.click(within(banner).getByRole("button", { name: "Převzít externí cenu", exact: true }));

  // Assert
  expect(mockResolveConflict).toHaveBeenCalledWith({
    productCode: "TON002030",
    target: "Flexi",
    resolution: "AcceptRemotePrice",
  });
});
