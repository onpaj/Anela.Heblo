import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import {
  usePricingBaselineQuery,
  usePricingSummaryQuery,
  useRecalculatePricingMutation,
  usePricingScenariosQuery,
  usePricingScenarioQuery,
  useSavePricingScenarioMutation,
  useDeletePricingScenarioMutation,
} from "../usePricingSimulator";
import { getAuthenticatedApiClient } from "../../client";
import { ProductType } from "../../generated/api-client";

jest.mock("../../client", () => ({
  ...jest.requireActual("../../client"),
  getAuthenticatedApiClient: jest.fn(),
}));

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        gcTime: 0,
      },
    },
  });

  return ({ children }: { children: React.ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);
};

describe("usePricingBaselineQuery", () => {
  it("passes the filter through to the generated client", async () => {
    const getBaseline = jest.fn().mockResolvedValue({ rows: [], totals: {} });
    (getAuthenticatedApiClient as jest.Mock).mockResolvedValue({
      pricingSimulator_GetBaseline: getBaseline,
    });

    const { result } = renderHook(
      () =>
        usePricingBaselineQuery({
          productCode: "DEO",
          productName: undefined,
          productType: undefined,
        }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Real generated signature: pricingSimulator_GetBaseline(productCode, productName, productType)
    // all typed `string | null | undefined` / `ProductType | null | undefined`.
    expect(getBaseline).toHaveBeenCalledWith("DEO", null, null);
  });

  it("refetches when the filter changes", async () => {
    const getBaseline = jest.fn().mockResolvedValue({ rows: [], totals: {} });
    (getAuthenticatedApiClient as jest.Mock).mockResolvedValue({
      pricingSimulator_GetBaseline: getBaseline,
    });

    const { rerender } = renderHook(
      ({ code }) =>
        usePricingBaselineQuery({
          productCode: code,
          productName: undefined,
          productType: undefined,
        }),
      { wrapper: createWrapper(), initialProps: { code: "DEO" } },
    );
    await waitFor(() => expect(getBaseline).toHaveBeenCalledTimes(1));

    rerender({ code: "KRE" });
    await waitFor(() => expect(getBaseline).toHaveBeenCalledTimes(2));
  });
});

describe("useRecalculatePricingMutation", () => {
  it("does not invalidate pricingBaseline on success", async () => {
    const recalculate = jest.fn().mockResolvedValue({ rows: [], totals: {}, overrides: [] });
    (getAuthenticatedApiClient as jest.Mock).mockResolvedValue({
      pricingSimulator_Recalculate: recalculate,
    });

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, gcTime: 0 } },
    });
    const invalidateSpy = jest.spyOn(queryClient, "invalidateQueries");
    const wrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client: queryClient }, children);

    const { result } = renderHook(() => useRecalculatePricingMutation(), { wrapper });

    result.current.mutate({
      productCode: "DEO",
      overrides: [],
      edit: null,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(recalculate).toHaveBeenCalledTimes(1);
    expect(invalidateSpy).not.toHaveBeenCalledWith(
      expect.objectContaining({ queryKey: expect.arrayContaining(["pricing-baseline"]) }),
    );
  });
});

describe("usePricingScenariosQuery", () => {
  it("fetches the scenario list from the generated client", async () => {
    const getScenarios = jest.fn().mockResolvedValue({ scenarios: [] });
    (getAuthenticatedApiClient as jest.Mock).mockResolvedValue({
      pricingScenarios_GetScenarios: getScenarios,
    });

    const { result } = renderHook(() => usePricingScenariosQuery(), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(getScenarios).toHaveBeenCalledTimes(1);
  });
});

describe("usePricingScenarioQuery", () => {
  it("fetches a single scenario by id", async () => {
    const getScenario = jest.fn().mockResolvedValue({
      scenario: { id: "abc" },
      rows: [],
      totals: {},
      overrides: [],
    });
    (getAuthenticatedApiClient as jest.Mock).mockResolvedValue({
      pricingScenarios_GetScenario: getScenario,
    });

    const { result } = renderHook(() => usePricingScenarioQuery("abc"), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(getScenario).toHaveBeenCalledWith("abc");
  });
});

describe("useSavePricingScenarioMutation", () => {
  it("invalidates pricingScenarios on success", async () => {
    const createScenario = jest.fn().mockResolvedValue({ id: "new-id" });
    (getAuthenticatedApiClient as jest.Mock).mockResolvedValue({
      pricingScenarios_CreateScenario: createScenario,
    });

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, gcTime: 0 } },
    });
    const invalidateSpy = jest.spyOn(queryClient, "invalidateQueries");
    const wrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client: queryClient }, children);

    const { result } = renderHook(() => useSavePricingScenarioMutation(), { wrapper });

    result.current.mutate({ name: "Scenario A", overrides: [] });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(createScenario).toHaveBeenCalledTimes(1);
    expect(invalidateSpy).toHaveBeenCalledWith(
      expect.objectContaining({ queryKey: ["pricing-scenarios"] }),
    );
  });
});

describe("useDeletePricingScenarioMutation", () => {
  it("invalidates pricingScenarios on success", async () => {
    const deleteScenario = jest.fn().mockResolvedValue({});
    (getAuthenticatedApiClient as jest.Mock).mockResolvedValue({
      pricingScenarios_DeleteScenario: deleteScenario,
    });

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, gcTime: 0 } },
    });
    const invalidateSpy = jest.spyOn(queryClient, "invalidateQueries");
    const wrapper = ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client: queryClient }, children);

    const { result } = renderHook(() => useDeletePricingScenarioMutation(), { wrapper });

    result.current.mutate("abc");

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(deleteScenario).toHaveBeenCalledWith("abc");
    expect(invalidateSpy).toHaveBeenCalledWith(
      expect.objectContaining({ queryKey: ["pricing-scenarios"] }),
    );
  });
});

// The summary band can cover more products than the grid does, so the scopes that
// reach past the name/code filter are served by a second calculation that drops that
// filter but keeps the product type and every override the user has made.
describe("usePricingSummaryQuery", () => {
  const mockRecalculate = () => {
    const recalculate = jest
      .fn()
      .mockResolvedValue({ rows: [], totals: {}, overrides: [] });
    (getAuthenticatedApiClient as jest.Mock).mockResolvedValue({
      pricingSimulator_Recalculate: recalculate,
    });
    return recalculate;
  };

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("asks for the whole catalogue, carrying the overrides but no name or code filter", async () => {
    // Arrange
    const recalculate = mockRecalculate();

    // Act
    const { result } = renderHook(
      () =>
        usePricingSummaryQuery(
          [{ productCode: "DEO", price: 120 }],
          undefined,
          true,
        ),
      { wrapper: createWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Assert: the request carries the override and nothing that would narrow the
    // population -- narrowing it is exactly what this second call exists to undo.
    // The generated request class declares every field, so what matters is what it
    // puts on the wire: toJSON is the shape the server actually receives.
    const request = recalculate.mock.calls[0][0].toJSON();
    expect(request.productType).toBeUndefined();
    expect(request.productCode).toBeUndefined();
    expect(request.productName).toBeUndefined();
    expect(request.overrides).toEqual([
      expect.objectContaining({ productCode: "DEO", price: 120 }),
    ]);
  });

  it("keeps the product type, which narrows the catalogue rather than the view", async () => {
    // Arrange
    const recalculate = mockRecalculate();

    // Act
    const { result } = renderHook(
      () => usePricingSummaryQuery([], ProductType.Product, true),
      { wrapper: createWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Assert
    expect(recalculate.mock.calls[0][0].productType).toBe(ProductType.Product);
  });

  it("prices nothing while the scope does not need it", async () => {
    // Arrange: the call covers the whole catalogue, so it must not run for a scope
    // the grid's own result already answers.
    const recalculate = mockRecalculate();

    // Act
    renderHook(() => usePricingSummaryQuery([], undefined, false), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(recalculate).not.toHaveBeenCalled());

    // Assert
    expect(recalculate).not.toHaveBeenCalled();
  });

  it("re-prices when the overrides change", async () => {
    // Arrange
    const recalculate = mockRecalculate();

    // Act
    const { rerender } = renderHook(
      ({ overrides }) => usePricingSummaryQuery(overrides, undefined, true),
      {
        wrapper: createWrapper(),
        initialProps: { overrides: [{ productCode: "DEO", price: 120 }] },
      },
    );
    await waitFor(() => expect(recalculate).toHaveBeenCalledTimes(1));
    rerender({ overrides: [{ productCode: "DEO", price: 130 }] });

    // Assert
    await waitFor(() => expect(recalculate).toHaveBeenCalledTimes(2));
  });

  it("does not re-price when the same overrides come back in a new array", async () => {
    // Arrange: a re-render alone must not cost a whole-catalogue calculation.
    const recalculate = mockRecalculate();

    // Act
    const { rerender } = renderHook(
      ({ overrides }) => usePricingSummaryQuery(overrides, undefined, true),
      {
        wrapper: createWrapper(),
        initialProps: { overrides: [{ productCode: "DEO", price: 120 }] },
      },
    );
    await waitFor(() => expect(recalculate).toHaveBeenCalledTimes(1));
    rerender({ overrides: [{ productCode: "DEO", price: 120 }] });

    // Assert
    await waitFor(() => expect(recalculate).toHaveBeenCalledTimes(1));
  });
});
