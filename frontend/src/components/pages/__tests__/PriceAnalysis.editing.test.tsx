import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import PriceAnalysis from "../PriceAnalysis";
import * as usePricingSimulatorHook from "../../../api/hooks/usePricingSimulator";
import {
  PricingRowDto,
  PricingTotalsDto,
  PricingEditField,
  SwaggerException,
} from "../../../api/generated/api-client";
import { formatCurrency } from "../../../utils/formatters";
import { toast } from "react-hot-toast";

// Editing/recalculation behaviours added in Task 9. The read-only rendering
// (filters, totals lines, excluded rows) is already covered by PriceAnalysis.test.tsx;
// this file is scoped to: commit-on-blur, Enter/Escape, rejected vs network failures,
// and per-row/global reset.
jest.mock("../../../api/hooks/usePricingSimulator");
jest.mock("react-hot-toast", () => ({
  __esModule: true,
  toast: { success: jest.fn(), error: jest.fn() },
}));

const mockUsePricingBaselineQuery =
  usePricingSimulatorHook.usePricingBaselineQuery as jest.MockedFunction<
    typeof usePricingSimulatorHook.usePricingBaselineQuery
  >;
const mockUseRecalculatePricingMutation =
  usePricingSimulatorHook.useRecalculatePricingMutation as jest.MockedFunction<
    typeof usePricingSimulatorHook.useRecalculatePricingMutation
  >;
// Task 10 wires PricingScenarioBar (save/load/delete) into PriceAnalysis; it always
// renders and always calls these four hooks, so every test needs a non-undefined
// return value even though this suite never drives scenario behaviour.
const mockUsePricingScenariosQuery =
  usePricingSimulatorHook.usePricingScenariosQuery as jest.MockedFunction<
    typeof usePricingSimulatorHook.usePricingScenariosQuery
  >;
const mockUsePricingScenarioQuery =
  usePricingSimulatorHook.usePricingScenarioQuery as jest.MockedFunction<
    typeof usePricingSimulatorHook.usePricingScenarioQuery
  >;
const mockUseSavePricingScenarioMutation =
  usePricingSimulatorHook.useSavePricingScenarioMutation as jest.MockedFunction<
    typeof usePricingSimulatorHook.useSavePricingScenarioMutation
  >;
const mockUseDeletePricingScenarioMutation =
  usePricingSimulatorHook.useDeletePricingScenarioMutation as jest.MockedFunction<
    typeof usePricingSimulatorHook.useDeletePricingScenarioMutation
  >;

// Plain object literals -- see PriceAnalysis.test.tsx for why (Babel class-field
// re-initialization drops constructor-assigned fields on the generated Response classes).
const buildRow = (overrides: Partial<PricingRowDto> = {}): PricingRowDto =>
  ({
    productCode: "PROD001",
    productName: "Test Product 1",
    baselinePrice: 150,
    baselineMaterialCost: 30,
    baselineManufacturingCost: 20,
    baselineQuantity: 100,
    price: 150,
    materialCost: 30,
    manufacturingCost: 20,
    forecastQuantity: 100,
    m0Amount: 120,
    m0Percentage: 80,
    m1Amount: 100,
    m1Percentage: 66.67,
    isEdited: false,
    isExcluded: false,
    baselineDrifted: false,
    ...overrides,
  }) as PricingRowDto;

const buildTotals = (overrides: Partial<PricingTotalsDto> = {}): PricingTotalsDto =>
  ({
    revenueBefore: 15000,
    revenueAfter: 15000,
    revenueDelta: 0,
    revenueDeltaPercentage: 0,
    m0Before: 12000,
    m0After: 12000,
    m0Delta: 0,
    m0DeltaPercentage: 0,
    m1Before: 10000,
    m1After: 10000,
    m1Delta: 0,
    m1DeltaPercentage: 0,
    editedProductCount: 0,
    excludedProductCount: 0,
    ...overrides,
  }) as PricingTotalsDto;

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>{children}</BrowserRouter>
    </QueryClientProvider>
  );
};

describe("PriceAnalysis editing", () => {
  let mockMutateAsync: jest.Mock;

  const priceInput = () =>
    screen.getByTestId(`pricing-cell-PROD001-${PricingEditField.Price}`) as HTMLInputElement;

  beforeEach(() => {
    jest.clearAllMocks();
    mockMutateAsync = jest.fn();
    mockUseRecalculatePricingMutation.mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: false,
    } as any);
    mockUsePricingBaselineQuery.mockReturnValue({
      data: { rows: [buildRow()], totals: buildTotals() },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);
    mockUsePricingScenariosQuery.mockReturnValue({
      data: { scenarios: [] },
      isLoading: false,
      error: null,
    } as any);
    mockUsePricingScenarioQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    } as any);
    mockUseSavePricingScenarioMutation.mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
    } as any);
    mockUseDeletePricingScenarioMutation.mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
    } as any);
  });

  it("does not call the mutation while typing, only on blur", async () => {
    mockMutateAsync.mockResolvedValue({
      rows: [buildRow({ price: 175, isEdited: true })],
      totals: buildTotals(),
      overrides: [{ productCode: "PROD001", price: 175 }],
    });

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "175" } });
    expect(mockMutateAsync).not.toHaveBeenCalled();

    fireEvent.blur(priceInput());
    expect(mockMutateAsync).toHaveBeenCalledTimes(1);

    // Wait for the full round trip (including the reset-token bump) to settle so
    // no state update from this commit leaks, unflushed, into the next test.
    expect(await screen.findByTestId("pricing-row-reset-PROD001")).toBeInTheDocument();
  });

  it("posts the accumulated overrides and the single edit gesture on commit", async () => {
    mockMutateAsync.mockResolvedValue({
      rows: [buildRow({ price: 175, isEdited: true })],
      totals: buildTotals(),
      overrides: [{ productCode: "PROD001", price: 175 }],
    });

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "175" } });
    fireEvent.blur(priceInput());

    expect(mockMutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({
        overrides: [],
        edit: { productCode: "PROD001", field: PricingEditField.Price, value: 175 },
      }),
    );

    expect(await screen.findByTestId("pricing-row-reset-PROD001")).toBeInTheDocument();
  });

  it("replaces rows, totals and overrides from a successful recalculate", async () => {
    mockMutateAsync.mockResolvedValue({
      rows: [buildRow({ price: 175, m0Percentage: 82 })],
      totals: buildTotals({ revenueAfter: 17500, revenueDelta: 2500 }),
      overrides: [{ productCode: "PROD001", price: 175 }],
    });

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "175" } });
    fireEvent.blur(priceInput());

    await waitFor(() => {
      const lineText = screen.getByTestId("totals-line-Obrat").textContent ?? "";
      // Read raw textContent and compare with .toContain rather than jest-dom's
      // toHaveTextContent: its default normalizer collapses formatCurrency's
      // non-breaking spaces to plain ASCII ones, which would silently break this
      // match (see PriceAnalysis.test.tsx's assertLine for the same pattern).
      expect(lineText).toContain(formatCurrency(17500));
    });

    const m0Input = screen.getByTestId(
      `pricing-cell-PROD001-${PricingEditField.M0Percentage}`,
    ) as HTMLInputElement;
    expect(m0Input.value).toBe("82");
  });

  it("shows the Czech message inline on a rejected edit, keeps the prior value, and leaves totals unchanged", async () => {
    const body = JSON.stringify({
      success: false,
      errorCode: "PricingNegativeMaterialCost",
      params: null,
    });
    mockMutateAsync.mockRejectedValue(new SwaggerException("Bad Request", 400, body, {}, null));

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "999" } });
    fireEvent.blur(priceInput());

    expect(
      await screen.findByText(
        "Marže M0 je vyšší než cena — materiálové náklady by byly záporné",
      ),
    ).toBeInTheDocument();

    // The error element appears on the render that sets the cell's `error` prop; the
    // cell's own draft resync is a passive effect that runs after it. Asserting the
    // input value synchronously after findByText therefore races that effect (observed
    // flaking under parallel suite load), so poll for it instead.
    await waitFor(() => expect(priceInput().value).toBe("150"));
    expect(screen.getByTestId("totals-line-Obrat").textContent ?? "").toContain(
      formatCurrency(15000),
    );
  });

  it("reverts the cell and badges totals as stale on a network failure", async () => {
    mockMutateAsync.mockRejectedValue(new Error("Network Error"));

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "999" } });
    fireEvent.blur(priceInput());

    await waitFor(() => {
      expect(toast.error).toHaveBeenCalled();
    });

    expect(priceInput().value).toBe("150");
    expect(screen.getByTestId("totals-stale-badge")).toBeInTheDocument();
  });

  it("keeps the previous totals rendered and shows a spinner while a recalculate is in flight", () => {
    mockUseRecalculatePricingMutation.mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: true,
    } as any);

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    // Previous totals must still be on screen -- never blanked during a request.
    expect(screen.getByTestId("totals-line-Obrat").textContent ?? "").toContain(
      formatCurrency(15000),
    );
    expect(screen.getByText(/Přepočítávám/i)).toBeInTheDocument();
  });

  it("clears a row's override and recalculates on per-row reset", async () => {
    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ price: 175, isEdited: true })],
      totals: buildTotals({ revenueAfter: 17500 }),
      overrides: [{ productCode: "PROD001", price: 175 }],
    });

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "175" } });
    fireEvent.blur(priceInput());

    const resetButton = await screen.findByTestId("pricing-row-reset-PROD001");

    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ price: 150, isEdited: false })],
      totals: buildTotals(),
      overrides: [],
    });

    fireEvent.click(resetButton);

    expect(mockMutateAsync).toHaveBeenLastCalledWith(
      expect.objectContaining({ overrides: [], edit: undefined }),
    );

    // The row's own reset control disappears once the server confirms it is
    // no longer edited -- proof the full round trip (not just the call) landed.
    await waitFor(() => {
      expect(screen.queryByTestId("pricing-row-reset-PROD001")).not.toBeInTheDocument();
    });
  });

  it("commits an M0 Kč margin edit with field M0Amount (the Kč side of the M0 Kč/% pair)", async () => {
    mockMutateAsync.mockResolvedValue({
      rows: [buildRow({ m0Amount: 140, isEdited: true })],
      totals: buildTotals(),
      overrides: [{ productCode: "PROD001", materialCost: 10 }],
    });

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    const m0AmountInput = screen.getByTestId(
      `pricing-cell-PROD001-${PricingEditField.M0Amount}`,
    );
    fireEvent.change(m0AmountInput, { target: { value: "140" } });
    fireEvent.blur(m0AmountInput);

    expect(mockMutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({
        edit: { productCode: "PROD001", field: PricingEditField.M0Amount, value: 140 },
      }),
    );

    expect(await screen.findByTestId("pricing-row-reset-PROD001")).toBeInTheDocument();
  });

  it("does not clobber a different cell's in-progress draft when another cell's commit settles", async () => {
    let resolveFirst: (value: unknown) => void = () => {};
    const firstPromise = new Promise((resolve) => {
      resolveFirst = resolve;
    });
    mockMutateAsync.mockReturnValueOnce(firstPromise);

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    // Commit A: Price, deliberately left in flight.
    fireEvent.change(priceInput(), { target: { value: "175" } });
    fireEvent.blur(priceInput());
    expect(mockMutateAsync).toHaveBeenCalledTimes(1);

    // Start typing into a DIFFERENT, unrelated cell -- never blurred, never
    // committed, still focused.
    const forecastInput = screen.getByTestId(
      `pricing-cell-PROD001-${PricingEditField.ForecastQuantity}`,
    ) as HTMLInputElement;
    // A real .focus() call (not fireEvent.focus, which only dispatches a synthetic
    // event without moving document.activeElement) so the assertion on focus below
    // is meaningful, and so PricingEditableCell's own isFocusedRef -- driven by a
    // real onFocus handler -- sees it too.
    forecastInput.focus();
    fireEvent.change(forecastInput, { target: { value: "999" } });

    // A settles successfully. Its response never touches forecastQuantity.
    resolveFirst({
      rows: [buildRow({ price: 175, isEdited: true })],
      totals: buildTotals(),
      overrides: [{ productCode: "PROD001", price: 175 }],
    });

    await waitFor(() => expect(priceInput().value).toBe("175"));

    // The other cell's in-progress, uncommitted draft -- and its focus -- must
    // have survived A's settle untouched.
    expect(forecastInput.value).toBe("999");
    expect(forecastInput).toHaveFocus();
    expect(mockMutateAsync).toHaveBeenCalledTimes(1);
  });

  it("does not post a stale value when a focused-but-untyped cell's value changes underneath it", async () => {
    mockMutateAsync.mockResolvedValueOnce({
      // Editing the M0 Kč amount recomputes M0 % on the same row -- the exact
      // sibling-recompute case the resync effect exists for.
      rows: [buildRow({ m0Amount: 140, m0Percentage: 82, isEdited: true })],
      totals: buildTotals(),
      overrides: [{ productCode: "PROD001", materialCost: 5 }],
    });

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    // The user clicks into M0 % -- but never types anything into it.
    const m0PercentageInput = screen.getByTestId(
      `pricing-cell-PROD001-${PricingEditField.M0Percentage}`,
    ) as HTMLInputElement;
    m0PercentageInput.focus();

    // A sibling field on the SAME row (M0 Kč) is committed instead.
    const m0AmountInput = screen.getByTestId(
      `pricing-cell-PROD001-${PricingEditField.M0Amount}`,
    );
    fireEvent.change(m0AmountInput, { target: { value: "140" } });
    fireEvent.blur(m0AmountInput);

    // Wait for the full round trip (not just the mock call count -- see the
    // round-1 report's note on that exact race) via a DOM consequence that can
    // only appear once the response has actually landed and re-rendered: the
    // row's own reset control, gated on the NEW row's isEdited flag. M0 %'s
    // draft was never resynced while it was focused, so it still shows the
    // OLD "80" internally at this point even though its `value` prop is now 82.
    expect(await screen.findByTestId("pricing-row-reset-PROD001")).toBeInTheDocument();

    // The user now clicks away from M0 % WITHOUT ever having typed into it.
    fireEvent.blur(m0PercentageInput);

    // No second edit must be posted -- the cell was never actually touched,
    // so a stale "80" must not be sent just because it differs from the new
    // "82". The cell must instead now display the server's fresh value.
    expect(mockMutateAsync).toHaveBeenCalledTimes(1);
    expect(m0PercentageInput.value).toBe("82");
  });

  it("serializes overlapping commits so the second carries the first commit's already-applied override", async () => {
    let resolveFirst: (value: unknown) => void = () => {};
    const firstPromise = new Promise((resolve) => {
      resolveFirst = resolve;
    });
    mockMutateAsync.mockReturnValueOnce(firstPromise);

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    // Commit A: Price, left in flight.
    fireEvent.change(priceInput(), { target: { value: "175" } });
    fireEvent.blur(priceInput());
    expect(mockMutateAsync).toHaveBeenCalledTimes(1);

    // Commit B: before A settles, edit a different field on the same row.
    const forecastInput = screen.getByTestId(
      `pricing-cell-PROD001-${PricingEditField.ForecastQuantity}`,
    );
    fireEvent.change(forecastInput, { target: { value: "120" } });
    fireEvent.blur(forecastInput);

    // B must be queued behind A, not sent yet.
    expect(mockMutateAsync).toHaveBeenCalledTimes(1);

    const firstResponseOverrides = [{ productCode: "PROD001", price: 175 }];
    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ price: 175, forecastQuantity: 120, isEdited: true })],
      totals: buildTotals(),
      overrides: [{ productCode: "PROD001", price: 175, forecastQuantity: 120 }],
    });

    resolveFirst({
      rows: [buildRow({ price: 175, isEdited: true })],
      totals: buildTotals(),
      overrides: firstResponseOverrides,
    });

    // Once A settles, B's queued request goes out -- and must carry A's
    // already-committed override, not the pre-A overrides captured when B fired
    // (which would silently discard A's edit).
    await waitFor(() => expect(mockMutateAsync).toHaveBeenCalledTimes(2));
    expect(mockMutateAsync).toHaveBeenLastCalledWith(
      expect.objectContaining({
        overrides: firstResponseOverrides,
        edit: {
          productCode: "PROD001",
          field: PricingEditField.ForecastQuantity,
          value: 120,
        },
      }),
    );
  });

  it("uses the server's authoritative overrides array -- not a client hand-merge -- for the next edit", async () => {
    // Includes an override for a product the client never touched itself, so this
    // can only pass if the client echoes the server's array verbatim rather than
    // re-deriving or hand-merging it from what it thinks it sent.
    const firstResponseOverrides = [
      { productCode: "PROD001", price: 175 },
      { productCode: "PROD999", forecastQuantity: 42 },
    ];
    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ price: 175, isEdited: true })],
      totals: buildTotals(),
      overrides: firstResponseOverrides,
    });

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "175" } });
    fireEvent.blur(priceInput());

    // Wait for the full first commit to settle (not just the mock call count):
    // the commit queue (see performRecalculate) only lets the SECOND commit fire
    // its request synchronously once the first has fully resolved, including its
    // own internal bookkeeping -- a plain call-count check can win a race against
    // that bookkeeping and make the second commit queue up instead of firing.
    await waitFor(() => expect(priceInput().value).toBe("175"));

    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ price: 175, forecastQuantity: 120, isEdited: true })],
      totals: buildTotals(),
      overrides: firstResponseOverrides,
    });

    const forecastInput = screen.getByTestId(
      `pricing-cell-PROD001-${PricingEditField.ForecastQuantity}`,
    );
    fireEvent.change(forecastInput, { target: { value: "120" } });
    fireEvent.blur(forecastInput);

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenLastCalledWith(
        expect.objectContaining({ overrides: firstResponseOverrides }),
      );
    });
  });

  // `rows` prefers the recalculated set over the baseline query, so before this fix the
  // first successful edit shadowed the baseline forever: applying or clearing a filter
  // refetched `data` and changed nothing at all on screen.
  it("re-runs the recalculation against the NEW filter after an edit, and updates the displayed rows", async () => {
    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ price: 175, isEdited: true })],
      totals: buildTotals(),
      overrides: [{ productCode: "PROD001", price: 175 }],
    });

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "175" } });
    fireEvent.blur(priceInput());
    await screen.findByTestId("pricing-row-reset-PROD001");
    expect(screen.getByText("Test Product 1")).toBeInTheDocument();

    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ productCode: "PROD002", productName: "Filtered Product" })],
      totals: buildTotals(),
      overrides: [{ productCode: "PROD001", price: 175 }],
    });

    fireEvent.change(screen.getByPlaceholderText("Název produktu..."), {
      target: { value: "Filtered" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Filtrovat" }));

    // The grid must now show the new filter's rows -- the whole point of the fix.
    await waitFor(() => {
      expect(screen.getByText("Filtered Product")).toBeInTheDocument();
    });
    expect(screen.queryByText("Test Product 1")).not.toBeInTheDocument();

    // ...and the request must carry the NEW filter value. The closure's `filter` still
    // holds the previous one in that tick, so reading it back from state would have
    // recalculated against the OLD filter and quietly produced the old rows again.
    expect(mockMutateAsync).toHaveBeenLastCalledWith(
      expect.objectContaining({
        productName: "Filtered",
        overrides: [{ productCode: "PROD001", price: 175 }],
        edit: undefined,
      }),
    );
  });

  it("falls back to the refetched baseline when the filter changes and no edits remain", async () => {
    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ price: 175, isEdited: true })],
      totals: buildTotals(),
      overrides: [{ productCode: "PROD001", price: 175 }],
    });

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "175" } });
    fireEvent.blur(priceInput());
    const resetAllButton = await screen.findByTestId("pricing-reset-all");

    // Reset everything: overrides are empty again, but `recalculated` still shadows
    // the baseline query.
    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ price: 150, isEdited: false })],
      totals: buildTotals(),
      overrides: [],
    });
    fireEvent.click(resetAllButton);
    await waitFor(() => {
      expect(screen.queryByTestId("pricing-reset-all")).not.toBeInTheDocument();
    });

    // The baseline query now answers with a different product.
    mockUsePricingBaselineQuery.mockReturnValue({
      data: {
        rows: [buildRow({ productCode: "PROD002", productName: "Filtered Product" })],
        totals: buildTotals(),
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    fireEvent.click(screen.getByRole("button", { name: "Vymazat" }));

    await waitFor(() => {
      expect(screen.getByText("Filtered Product")).toBeInTheDocument();
    });
    expect(screen.queryByText("Test Product 1")).not.toBeInTheDocument();
    // Nothing left to replay, so no third recalculation was fired.
    expect(mockMutateAsync).toHaveBeenCalledTimes(2);
  });

  it("clears a stale cell error once a DIFFERENT cell's edit succeeds", async () => {
    const body = JSON.stringify({
      success: false,
      errorCode: "PricingNegativeMaterialCost",
      params: null,
    });
    mockMutateAsync.mockRejectedValueOnce(
      new SwaggerException("Bad Request", 400, body, {}, null),
    );

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "999" } });
    fireEvent.blur(priceInput());

    const priceErrorTestId = `pricing-cell-error-PROD001-${PricingEditField.Price}`;
    expect(await screen.findByTestId(priceErrorTestId)).toBeInTheDocument();

    // A successful edit on another cell re-derives every row server-side, so the
    // rejected value is gone -- the red ring and message must go with it. Only the
    // committed cell's own key is cleared up-front, so this can only pass if the
    // success path clears the rest.
    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ forecastQuantity: 120, isEdited: true })],
      totals: buildTotals(),
      overrides: [{ productCode: "PROD001", forecastQuantity: 120 }],
    });

    const forecastInput = screen.getByTestId(
      `pricing-cell-PROD001-${PricingEditField.ForecastQuantity}`,
    );
    fireEvent.change(forecastInput, { target: { value: "120" } });
    fireEvent.blur(forecastInput);

    await waitFor(() => {
      expect(screen.queryByTestId(priceErrorTestId)).not.toBeInTheDocument();
    });
  });

  it("clears every override on global reset", async () => {
    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ price: 175, isEdited: true })],
      totals: buildTotals({ revenueAfter: 17500 }),
      overrides: [{ productCode: "PROD001", price: 175 }],
    });

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "175" } });
    fireEvent.blur(priceInput());

    const resetAllButton = await screen.findByTestId("pricing-reset-all");

    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ price: 150, isEdited: false })],
      totals: buildTotals(),
      overrides: [],
    });

    fireEvent.click(resetAllButton);

    expect(mockMutateAsync).toHaveBeenLastCalledWith(
      expect.objectContaining({ overrides: [], edit: undefined }),
    );

    // The global reset control disappears once overrides comes back empty --
    // proof the full round trip (not just the call) landed.
    await waitFor(() => {
      expect(screen.queryByTestId("pricing-reset-all")).not.toBeInTheDocument();
    });
  });
});
