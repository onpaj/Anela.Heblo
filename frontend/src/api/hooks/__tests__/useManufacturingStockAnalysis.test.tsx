import { renderHook, waitFor } from "@testing-library/react";
import {
  useManufacturingStockAnalysisQuery,
  TimePeriodFilter,
  ManufacturingStockSortBy,
  calculateTimePeriodRange,
  formatWarehouseStock,
} from "../useManufacturingStockAnalysis";
import {
  mockAuthenticatedApiClient,
  createQueryClientWrapper,
} from "../../testUtils";

jest.mock("../../client");

describe("useManufacturingStockAnalysisQuery", () => {
  let mockClient: { manufacturingStockAnalysis_GetStockAnalysis: jest.Mock };

  beforeEach(() => {
    jest.clearAllMocks();
    mockClient = { manufacturingStockAnalysis_GetStockAnalysis: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  const mockResponse = {
    items: [
      {
        code: "TEST001",
        name: "Test Product",
        currentStock: 100,
        salesInPeriod: 50,
        dailySalesRate: 2.5,
        optimalDaysSetup: 20,
        stockDaysAvailable: 40,
        minimumStock: 10,
        overstockPercentage: 200,
        batchSize: "25",
        productFamily: "TestFamily",
        severity: "Adequate",
        isConfigured: true,
      },
    ],
    summary: {
      totalProducts: 1,
      criticalCount: 0,
      majorCount: 0,
      minorCount: 0,
      adequateCount: 1,
      unconfiguredCount: 0,
      analysisPeriodStart: "2023-01-01T00:00:00Z",
      analysisPeriodEnd: "2023-03-31T23:59:59Z",
      productFamilies: ["TestFamily"],
    },
    totalCount: 1,
    pageNumber: 1,
    pageSize: 20,
  };

  it("calls manufacturingStockAnalysis_GetStockAnalysis with params in exact declared positional order", async () => {
    mockClient.manufacturingStockAnalysis_GetStockAnalysis.mockResolvedValue(
      mockResponse,
    );

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(
      () =>
        useManufacturingStockAnalysisQuery({
          timePeriod: TimePeriodFilter.PreviousQuarter,
          pageNumber: 2,
          pageSize: 10,
          searchTerm: "test",
          criticalItemsOnly: true,
          productFamily: "TestFamily",
          sortBy: ManufacturingStockSortBy.CurrentStock,
          sortDescending: true,
          salesMultiplier: 1.5,
        }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Argument order guards against the positional-transposition risk flagged in
    // arch-review.r1.md's risk table (e.g. swapping pageNumber/pageSize, or the four
    // *ItemsOnly booleans).
    expect(
      mockClient.manufacturingStockAnalysis_GetStockAnalysis,
    ).toHaveBeenCalledWith(
      "PreviousQuarter", // timePeriod
      undefined, // customFromDate
      undefined, // customToDate
      "TestFamily", // productFamily
      true, // criticalItemsOnly
      undefined, // majorItemsOnly
      undefined, // adequateItemsOnly
      undefined, // unconfiguredOnly
      "test", // searchTerm
      2, // pageNumber
      10, // pageSize
      "CurrentStock", // sortBy
      true, // sortDescending
      1.5, // salesMultiplier
      false, // isExport
    );
    expect(result.current.data).toEqual(mockResponse);
  });

  it("handles API errors correctly", async () => {
    mockClient.manufacturingStockAnalysis_GetStockAnalysis.mockRejectedValue(
      new Error("An unexpected server error occurred."),
    );

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(
      () =>
        useManufacturingStockAnalysisQuery({
          timePeriod: TimePeriodFilter.PreviousQuarter,
          pageNumber: 1,
          pageSize: 20,
        }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(result.current.error).toBeTruthy();
    expect(result.current.data).toBeUndefined();
  });

  it("omits timePeriod param when it equals Q9M (default)", async () => {
    mockClient.manufacturingStockAnalysis_GetStockAnalysis.mockResolvedValue(
      mockResponse,
    );

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(
      () =>
        useManufacturingStockAnalysisQuery({
          timePeriod: TimePeriodFilter.Q9M,
          pageNumber: 1,
          pageSize: 20,
        }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const timePeriodArg =
      mockClient.manufacturingStockAnalysis_GetStockAnalysis.mock.calls[0][0];
    expect(timePeriodArg).toBeUndefined();
  });

  it("includes timePeriod param for non-default periods", async () => {
    mockClient.manufacturingStockAnalysis_GetStockAnalysis.mockResolvedValue(
      mockResponse,
    );

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(
      () =>
        useManufacturingStockAnalysisQuery({
          timePeriod: TimePeriodFilter.PreviousQuarter,
          pageNumber: 1,
          pageSize: 20,
        }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const timePeriodArg =
      mockClient.manufacturingStockAnalysis_GetStockAnalysis.mock.calls[0][0];
    expect(timePeriodArg).toBe("PreviousQuarter");
  });

  it("passes customFromDate/customToDate through as Date objects for CustomPeriod", async () => {
    const customFromDate = new Date("2023-01-01");
    const customToDate = new Date("2023-03-31");
    mockClient.manufacturingStockAnalysis_GetStockAnalysis.mockResolvedValue(
      mockResponse,
    );

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(
      () =>
        useManufacturingStockAnalysisQuery({
          timePeriod: TimePeriodFilter.CustomPeriod,
          customFromDate,
          customToDate,
          pageNumber: 1,
          pageSize: 20,
        }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(
      mockClient.manufacturingStockAnalysis_GetStockAnalysis,
    ).toHaveBeenCalledWith(
      "CustomPeriod",
      customFromDate,
      customToDate,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      1,
      20,
      undefined,
      undefined,
      undefined,
      false,
    );
  });
});

describe("calculateTimePeriodRange", () => {
  const now = new Date("2023-04-15"); // Mid April 2023

  beforeAll(() => {
    jest.useFakeTimers();
    jest.setSystemTime(now);
  });

  afterAll(() => {
    jest.useRealTimers();
  });

  it("calculates previous quarter correctly", () => {
    const result = calculateTimePeriodRange(TimePeriodFilter.PreviousQuarter);

    expect(result).not.toBeNull();
    expect(result!.fromDate.getMonth()).toBe(0); // January (0-indexed)
    expect(result!.fromDate.getFullYear()).toBe(2023);
    expect(result!.toDate.getMonth()).toBe(2); // March (0-indexed)
    expect(result!.toDate.getFullYear()).toBe(2023);
  });

  it("calculates future quarter correctly", () => {
    const result = calculateTimePeriodRange(TimePeriodFilter.FutureQuarter);

    expect(result).not.toBeNull();
    expect(result!.fromDate.getMonth()).toBe(3); // April (0-indexed)
    expect(result!.fromDate.getFullYear()).toBe(2022); // Previous year
    expect(result!.toDate.getMonth()).toBe(5); // June (0-indexed)
    expect(result!.toDate.getFullYear()).toBe(2022); // Previous year
  });

  it("calculates previous season correctly", () => {
    const result = calculateTimePeriodRange(TimePeriodFilter.PreviousSeason);

    expect(result).not.toBeNull();
    expect(result!.fromDate.getMonth()).toBe(9); // October (0-indexed)
    expect(result!.fromDate.getFullYear()).toBe(2022); // Previous year for season
    expect(result!.toDate.getMonth()).toBe(0); // January (0-indexed)
    expect(result!.toDate.getFullYear()).toBe(2023); // Next year from season start
  });

  it("returns null for custom period", () => {
    const result = calculateTimePeriodRange(TimePeriodFilter.CustomPeriod);

    expect(result).toBeNull();
  });

  it("calculates Q9M with two ranges", () => {
    const result = calculateTimePeriodRange(TimePeriodFilter.Q9M);

    expect(result).not.toBeNull();
    expect(result!.ranges).toHaveLength(2);

    // Range A: last 6 months → now
    const rangeA = result!.ranges![0];
    expect(rangeA.from.getFullYear()).toBe(2022);
    expect(rangeA.from.getMonth()).toBe(9); // October (0-indexed)
    expect(rangeA.from.getDate()).toBe(15);
    expect(rangeA.to).toEqual(now);

    // Range B: 1 year ago → 1 year ago + 3 months
    const rangeB = result!.ranges![1];
    expect(rangeB.from.getFullYear()).toBe(2022);
    expect(rangeB.from.getMonth()).toBe(3); // April (0-indexed)
    expect(rangeB.from.getDate()).toBe(15);
    expect(rangeB.to.getFullYear()).toBe(2022);
    expect(rangeB.to.getMonth()).toBe(6); // July (0-indexed)
    expect(rangeB.to.getDate()).toBe(15);

    // Outer bounds via primary (range A: sixMonthsAgo → now)
    expect(result!.fromDate).toEqual(rangeA.from);
    expect(result!.toDate).toEqual(now);
  });
});

describe("formatWarehouseStock", () => {
  const baseItem = {
    code: "P1",
    name: "Product 1",
    currentStock: 0,
    erpStock: 0,
    eshopStock: 0,
    transportStock: 0,
    manufacturedStock: 0,
    primaryStockSource: "Erp",
    reserve: 0,
    quarantine: 0,
    planned: 0,
    salesInPeriod: 0,
    dailySalesRate: 0,
    optimalDaysSetup: 0,
    stockDaysAvailable: 0,
    minimumStock: 0,
    overstockPercentage: 0,
    batchSize: "1",
    severity: "Adequate",
    isConfigured: true,
  } as any;

  it("shows only the total when transport and manufactured are both zero", () => {
    const item = { ...baseItem, currentStock: 5, erpStock: 5 };
    expect(formatWarehouseStock(item)).toBe("5");
  });

  it("shows primary+transport breakdown when only transport is non-zero", () => {
    const item = { ...baseItem, currentStock: 12, erpStock: 5, transportStock: 7 };
    expect(formatWarehouseStock(item)).toBe("12 (5+7)");
  });

  it("shows primary+manufactured breakdown when only manufactured is non-zero", () => {
    const item = { ...baseItem, currentStock: 8, erpStock: 5, manufacturedStock: 3 };
    expect(formatWarehouseStock(item)).toBe("8 (5+3)");
  });

  it("shows primary+transport+manufactured breakdown when both are non-zero", () => {
    const item = {
      ...baseItem,
      currentStock: 15,
      erpStock: 5,
      transportStock: 7,
      manufacturedStock: 3,
    };
    expect(formatWarehouseStock(item)).toBe("15 (5+7+3)");
  });
});
