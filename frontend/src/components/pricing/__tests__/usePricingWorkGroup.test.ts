import { act, renderHook } from "@testing-library/react";
import { usePricingWorkGroup } from "../usePricingWorkGroup";
import { PRICING_WORK_GROUP_STORAGE_KEY } from "../pricingWorkGroup";

const storedCodes = (): string[] =>
  JSON.parse(window.localStorage.getItem(PRICING_WORK_GROUP_STORAGE_KEY) ?? "[]");

describe("usePricingWorkGroup", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it("starts from whatever the last session pinned", () => {
    // Arrange
    window.localStorage.setItem(
      PRICING_WORK_GROUP_STORAGE_KEY,
      JSON.stringify(["PROD001"]),
    );

    // Act
    const { result } = renderHook(() => usePricingWorkGroup());

    // Assert
    expect(result.current.pinnedProductCodes.has("PROD001")).toBe(true);
  });

  it("writes every pin straight through to storage", () => {
    // Arrange
    const { result } = renderHook(() => usePricingWorkGroup());

    // Act
    act(() => result.current.toggleProductCode("PROD001"));

    // Assert
    expect(storedCodes()).toEqual(["PROD001"]);
  });

  it("composes two changes made before the next render", () => {
    // Arrange: both calls resolve against the same render, which is exactly the case
    // that used to lose the first change -- including in storage, so the loss
    // survived a reload.
    const { result } = renderHook(() => usePricingWorkGroup());

    // Act
    act(() => {
      result.current.toggleProductCode("PROD001");
      result.current.toggleProductCode("PROD002");
    });

    // Assert
    expect(Array.from(result.current.pinnedProductCodes)).toEqual([
      "PROD001",
      "PROD002",
    ]);
    expect(storedCodes()).toEqual(["PROD001", "PROD002"]);
  });

  it("composes a bulk pin made in the same tick as a single one", () => {
    // Arrange
    const { result } = renderHook(() => usePricingWorkGroup());

    // Act
    act(() => {
      result.current.toggleProductCode("PROD001");
      result.current.setProductCodes(["PROD002", "PROD003"], true);
    });

    // Assert
    expect(storedCodes()).toEqual(["PROD001", "PROD002", "PROD003"]);
  });

  it("unpins a code that is already in the group", () => {
    // Arrange
    const { result } = renderHook(() => usePricingWorkGroup());
    act(() => result.current.toggleProductCode("PROD001"));

    // Act
    act(() => result.current.toggleProductCode("PROD001"));

    // Assert
    expect(result.current.pinnedProductCodes.size).toBe(0);
    expect(storedCodes()).toEqual([]);
  });
});
