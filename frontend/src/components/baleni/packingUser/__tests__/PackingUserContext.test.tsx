import { act, renderHook } from "@testing-library/react";
import type { ReactNode } from "react";
import { PackingUserProvider, usePackingUser } from "../PackingUserContext";

const wrapper = ({ children }: { children: ReactNode }) => (
  <PackingUserProvider>{children}</PackingUserProvider>
);

describe("PackingUserContext", () => {
  beforeEach(() => localStorage.clear());

  test("persists the selected operator to localStorage", () => {
    const { result } = renderHook(() => usePackingUser(), { wrapper });
    act(() => result.current.setCurrent({ id: "u1", displayName: "Pepa" }));
    expect(result.current.current).toEqual({ id: "u1", displayName: "Pepa" });
    expect(localStorage.getItem("heblo.baleni.packingUser")).toContain("Pepa");
  });

  test("restores the operator from localStorage on mount", () => {
    localStorage.setItem(
      "heblo.baleni.packingUser",
      JSON.stringify({ id: "u2", displayName: "Jana" }),
    );
    const { result } = renderHook(() => usePackingUser(), { wrapper });
    expect(result.current.current).toEqual({ id: "u2", displayName: "Jana" });
  });

  test("clear removes the operator", () => {
    const { result } = renderHook(() => usePackingUser(), { wrapper });
    act(() => result.current.setCurrent({ id: "u1", displayName: "Pepa" }));
    act(() => result.current.clear());
    expect(result.current.current).toBeNull();
    expect(localStorage.getItem("heblo.baleni.packingUser")).toBeNull();
  });
});
