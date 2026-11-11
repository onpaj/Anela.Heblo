import { renderHook, act } from "@testing-library/react";
import { usePersistedNumber } from "../usePersistedNumber";

describe("usePersistedNumber", () => {
  beforeEach(() => window.localStorage.clear());

  it("returns the default when nothing is stored", () => {
    const { result } = renderHook(() => usePersistedNumber("k", 1.3));
    expect(result.current[0]).toBe(1.3);
  });

  it("restores a previously stored value", () => {
    window.localStorage.setItem("k", "1.2");
    const { result } = renderHook(() => usePersistedNumber("k", 1.0));
    expect(result.current[0]).toBe(1.2);
  });

  it("falls back to the default when the stored value is outside the bounds", () => {
    window.localStorage.setItem("k", "0");
    const { result } = renderHook(() => usePersistedNumber("k", 1.3, { min: 0.1, max: 9.9 }));
    expect(result.current[0]).toBe(1.3);
  });

  it("persists updates", () => {
    const { result } = renderHook(() => usePersistedNumber("k", 1.0));
    act(() => result.current[1](1.5));
    expect(result.current[0]).toBe(1.5);
    expect(window.localStorage.getItem("k")).toBe("1.5");
  });

  it("falls back to the default when the stored value is not a number", () => {
    window.localStorage.setItem("k", "abc");
    const { result } = renderHook(() => usePersistedNumber("k", 1.0));
    expect(result.current[0]).toBe(1.0);
  });

  it("still works when localStorage throws", () => {
    jest.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("blocked"); });
    jest.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("blocked"); });
    const { result } = renderHook(() => usePersistedNumber("k", 1.0));
    act(() => result.current[1](2));
    expect(result.current[0]).toBe(2);
    jest.restoreAllMocks();
  });
});
