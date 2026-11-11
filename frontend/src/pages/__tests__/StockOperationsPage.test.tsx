import React from "react";
import { render, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import StockOperationsPage from "../StockOperationsPage";
import { useStockUpOperationsQuery } from "../../api/hooks/useStockUpOperations";

jest.mock("../../api/hooks/useStockUpOperations", () => ({
  useStockUpOperationsQuery: jest.fn(),
  useRetryStockUpOperationMutation: jest.fn(() => ({
    mutate: jest.fn(),
    mutateAsync: jest.fn(),
    isPending: false,
  })),
  useAcceptStockUpOperationMutation: jest.fn(() => ({
    mutate: jest.fn(),
    mutateAsync: jest.fn(),
    isPending: false,
  })),
}));

jest.mock("../../components/common/CatalogAutocomplete", () => ({
  CatalogAutocomplete: () => <div data-testid="catalog-autocomplete" />,
}));

jest.mock("../../telemetry/useScreenView", () => ({
  useScreenView: jest.fn(),
}));

const mockedQuery = useStockUpOperationsQuery as jest.Mock;

const lastArgs = () => {
  const calls = mockedQuery.mock.calls;
  return calls[calls.length - 1][0];
};

const renderAt = (search: string) =>
  render(
    <MemoryRouter initialEntries={["/stock-operations" + search]}>
      <StockOperationsPage />
    </MemoryRouter>,
  );

describe("StockOperationsPage URL param initialization", () => {
  beforeEach(() => {
    mockedQuery.mockReset();
    mockedQuery.mockReturnValue({
      data: { operations: [], totalCount: 0, totalPages: 0, pageNumber: 1 },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    });
  });

  it("uses defaults when there are no URL params", async () => {
    renderAt("");

    await waitFor(() => {
      expect(lastArgs()).toMatchObject({
        state: "Active",
        page: 1,
        sortBy: "createdAt",
        sortDescending: true,
        pageSize: 50,
      });
    });
  });

  it("initializes all filters from URL params", async () => {
    renderAt(
      "?state=Failed&sourceType=TransportBox&sourceId=42&productCode=ABC" +
        "&documentNumber=D1&createdFrom=2026-01-01&createdTo=2026-01-31" +
        "&sortBy=state&sortDescending=false&page=3",
    );

    await waitFor(() => {
      expect(lastArgs().state).toBe("Failed");
    });

    const args = lastArgs();
    expect(args.sourceType).toBe("TransportBox");
    expect(args.sourceId).toBe(42);
    expect(args.productCode).toBe("ABC");
    expect(args.documentNumber).toBe("D1");
    expect(args.createdFrom).toBeInstanceOf(Date);
    expect(args.createdTo).toBeInstanceOf(Date);
    expect(args.createdFrom.toISOString().slice(0, 10)).toBe("2026-01-01");
    expect(args.createdTo.toISOString().slice(0, 10)).toBe("2026-01-31");
    expect(args.sortBy).toBe("state");
    expect(args.sortDescending).toBe(false);
    expect(args.page).toBe(3);
  });

  it("treats sourceType=All as no source type filter", async () => {
    renderAt("?sourceType=All&state=Failed");

    await waitFor(() => {
      expect(lastArgs().state).toBe("Failed");
    });
    expect(lastArgs().sourceType).toBeUndefined();
  });

  it("parses sortDescending=true as true", async () => {
    renderAt("?sortDescending=true&sortBy=state");

    await waitFor(() => {
      expect(lastArgs().sortBy).toBe("state");
    });
    expect(lastArgs().sortDescending).toBe(true);
  });

  // Characterization test: pins CURRENT behavior for invalid numeric params.
  // parseInt("abc") yields NaN and is passed straight through to the query.
  // This is a known gap and is intentionally not fixed here.
  it("passes NaN through for invalid page and sourceId (current behavior)", async () => {
    renderAt("?page=abc&sourceId=abc");

    await waitFor(() => {
      expect(Number.isNaN(lastArgs().page)).toBe(true);
    });
    expect(Number.isNaN(lastArgs().sourceId)).toBe(true);
  });
});
