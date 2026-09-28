import { mapMarginDataToMonthlyArrays } from "../MarginsChart.utils";

describe("mapMarginDataToMonthlyArrays", () => {
  it("returns 12 zeros for all 8 arrays when marginHistory is empty", () => {
    const result = mapMarginDataToMonthlyArrays([], new Date(2026, 6, 15));

    expect(result.m0PercentageData).toEqual(new Array(12).fill(0));
    expect(result.m1PercentageData).toEqual(new Array(12).fill(0));
    expect(result.m2PercentageData).toEqual(new Array(12).fill(0));
    expect(result.m3PercentageData).toEqual(new Array(12).fill(0));
    expect(result.m0CostLevelData).toEqual(new Array(12).fill(0));
    expect(result.m1CostLevelData).toEqual(new Array(12).fill(0));
    expect(result.m2CostLevelData).toEqual(new Array(12).fill(0));
    expect(result.m3CostLevelData).toEqual(new Array(12).fill(0));
  });
});
