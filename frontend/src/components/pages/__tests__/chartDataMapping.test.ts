import {
  CatalogSalesRecordDto,
  CatalogConsumedRecordDto,
} from "../../../api/hooks/useCatalog";

// Extract the mapping function for testing
const mapDataToMonthlyArray = (
  data: CatalogSalesRecordDto[] | CatalogConsumedRecordDto[],
  valueKey: "amountTotal" | "amount",
) => {
  const monthlyData = new Array(13).fill(0);
  const now = new Date();
  const currentYear = now.getFullYear();
  const currentMonth = now.getMonth() + 1; // JavaScript months are 0-based, convert to 1-based

  // Create a map for quick lookup of data by year-month key
  const dataMap = new Map<string, number>();
  data.forEach((record) => {
    const key = `${record.year}-${record.month}`;
    const value = (record as any)[valueKey] || 0;
    dataMap.set(key, value);
  });

  // Fill the array with data for the last 13 months
  for (let i = 0; i < 13; i++) {
    const monthsBack = 12 - i; // 12 months back to current month
    let adjustedYear = currentYear;
    let adjustedMonth = currentMonth - monthsBack;

    // Handle year transitions
    if (adjustedMonth <= 0) {
      adjustedYear--;
      adjustedMonth += 12;
    }

    const key = `${adjustedYear}-${adjustedMonth}`;
    const value = dataMap.get(key) || 0;
    monthlyData[i] = value;
  }
  return monthlyData;
};

describe("Chart Data Mapping", () => {
  beforeEach(() => {
    // Mock current date to August 1, 2025 for consistent testing
    jest.useFakeTimers();
    jest.setSystemTime(new Date("2025-08-01"));
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it("should map sales data correctly for current and previous months", () => {
    const salesData: CatalogSalesRecordDto[] = [
      {
        year: 2025,
        month: 8,
        amountTotal: 50,
        amountB2B: 30,
        amountB2C: 20,
        sumTotal: 5000,
        sumB2B: 3000,
        sumB2C: 2000,
      }, // Current month
      {
        year: 2025,
        month: 7,
        amountTotal: 40,
        amountB2B: 25,
        amountB2C: 15,
        sumTotal: 4000,
        sumB2B: 2500,
        sumB2C: 1500,
      }, // Last month
      {
        year: 2024,
        month: 9,
        amountTotal: 35,
        amountB2B: 20,
        amountB2C: 15,
        sumTotal: 3500,
        sumB2B: 2000,
        sumB2C: 1500,
      }, // 11 months ago
      {
        year: 2024,
        month: 8,
        amountTotal: 30,
        amountB2B: 18,
        amountB2C: 12,
        sumTotal: 3000,
        sumB2B: 1800,
        sumB2C: 1200,
      }, // 12 months ago
    ];

    const result = mapDataToMonthlyArray(salesData, "amountTotal");

    expect(result).toHaveLength(13);

    // Should have data for August 2024 (12 months ago) at position 0
    expect(result[0]).toBe(30);

    // Should have data for September 2024 (11 months ago) at position 1
    expect(result[1]).toBe(35);

    // Should have zeros for months without data
    expect(result[2]).toBe(0); // October 2024
    expect(result[3]).toBe(0); // November 2024

    // Should have data for July 2025 (1 month ago) at position 11
    expect(result[11]).toBe(40);

    // Should have data for August 2025 (current month) at position 12
    expect(result[12]).toBe(50);
  });

  it("should map consumption data correctly", () => {
    const consumedData: CatalogConsumedRecordDto[] = [
      { year: 2025, month: 8, amount: 25, productName: "Test Material" },
      { year: 2025, month: 6, amount: 15, productName: "Test Material" },
      { year: 2024, month: 10, amount: 20, productName: "Test Material" },
    ];

    const result = mapDataToMonthlyArray(consumedData, "amount");

    expect(result).toHaveLength(13);

    // Should have data for October 2024 (10 months ago) at position 2
    expect(result[2]).toBe(20);

    // Should have data for June 2025 (2 months ago) at position 10
    expect(result[10]).toBe(15);

    // Should have data for August 2025 (current month) at position 12
    expect(result[12]).toBe(25);

    // Other positions should be zero
    expect(result[5]).toBe(0);
    expect(result[8]).toBe(0);
  });

  it("should handle empty data array", () => {
    const result = mapDataToMonthlyArray([], "amountTotal");

    expect(result).toHaveLength(13);
    expect(result.every((value) => value === 0)).toBe(true);
  });

  it("should handle year transitions correctly", () => {
    // Test when current date is January (month transitions to previous year)
    jest.setSystemTime(new Date("2025-01-15"));

    const salesData: CatalogSalesRecordDto[] = [
      {
        year: 2024,
        month: 2,
        amountTotal: 100,
        amountB2B: 60,
        amountB2C: 40,
        sumTotal: 10000,
        sumB2B: 6000,
        sumB2C: 4000,
      }, // 11 months ago
      {
        year: 2024,
        month: 1,
        amountTotal: 80,
        amountB2B: 50,
        amountB2C: 30,
        sumTotal: 8000,
        sumB2B: 5000,
        sumB2C: 3000,
      }, // 12 months ago
    ];

    const result = mapDataToMonthlyArray(salesData, "amountTotal");

    // January 2024 (12 months ago) should be at position 0
    expect(result[0]).toBe(80);

    // February 2024 (11 months ago) should be at position 1
    expect(result[1]).toBe(100);

    // Rest should be zeros
    expect(result.slice(2, 12).every((value) => value === 0)).toBe(true);
  });

  it("should handle data with zero values", () => {
    const salesData: CatalogSalesRecordDto[] = [
      {
        year: 2025,
        month: 8,
        amountTotal: 0,
        amountB2B: 0,
        amountB2C: 0,
        sumTotal: 0,
        sumB2B: 0,
        sumB2C: 0,
      },
      {
        year: 2025,
        month: 7,
        amountTotal: 50,
        amountB2B: 30,
        amountB2C: 20,
        sumTotal: 5000,
        sumB2B: 3000,
        sumB2C: 2000,
      },
    ];

    const result = mapDataToMonthlyArray(salesData, "amountTotal");

    // Should preserve zero values
    expect(result[12]).toBe(0); // Current month with zero
    expect(result[11]).toBe(50); // Previous month with data
  });

  it("should handle missing properties gracefully", () => {
    const malformedData = [
      { year: 2025, month: 8 } as any, // Missing amountTotal
      {
        year: 2025,
        month: 7,
        amountTotal: 30,
        amountB2B: 20,
        amountB2C: 10,
        sumTotal: 3000,
        sumB2B: 2000,
        sumB2C: 1000,
      },
    ];

    const result = mapDataToMonthlyArray(malformedData, "amountTotal");

    // Should default to 0 for missing properties
    expect(result[12]).toBe(0); // Current month with missing amountTotal
    expect(result[11]).toBe(30); // Previous month with valid data
  });
});
